using Microsoft.EntityFrameworkCore; // Keep for potential future use? Or remove if GetChatHistory is gone? Keep for now.
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat; // For Ollama Role
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
// using System.Text.Json; // Removed
using Newtonsoft.Json; // Added for Json.NET
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
// using OpenAIChat = OpenAI.Chat; // No longer needed if GetChatHistory is removed

namespace TelegramSearchBot.Service.AI.LLM
{
    public class OllamaService : IService, ILLMService
    {
        public string ServiceName => "OllamaService";

        private readonly ILogger<OllamaService> _logger;
        private readonly DataDbContext _dbContext; // Keep for now, might be needed by other methods or future tools
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _availableToolsPromptPart;
        public string BotName { get; set; }

        public OllamaService(DataDbContext context, ILogger<OllamaService> logger, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _dbContext = context; // Keep DbContext injection
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;

            McpToolHelper.Initialize(_serviceProvider, _logger);

            _availableToolsPromptPart = McpToolHelper.RegisterToolsAndGetPromptString(Assembly.GetExecutingAssembly());
            if (string.IsNullOrWhiteSpace(_availableToolsPromptPart))
            {
                _availableToolsPromptPart = "<!-- No tools are currently available. -->";
            }
        }

        // --- Helper methods ---

        public async Task<bool> CheckAndPullModelAsync(OllamaApiClient ollama, string modelName)
        {
            _logger.LogInformation("Checking for Ollama model: {ModelName}", modelName);
            try
            {
                var models = await ollama.ListLocalModelsAsync();
                if (models.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("Model {ModelName} found locally.", modelName);
                    return true;
                }

                _logger.LogInformation("Model {ModelName} not found locally. Pulling...", modelName);
                
                // Consume the stream from PullModelAsync
                // Assuming the second parameter is CancellationToken, not a progress lambda
                await foreach (var status in ollama.PullModelAsync(modelName, System.Threading.CancellationToken.None))
                {
                    // Log status updates received from the stream
                    if (status != null) {
                         // Adjust property names (Percent, Status) if they differ in your OllamaSharp version
                         // Removed .HasValue check as status.Percent is likely double, not double?
                         _logger.LogInformation("[{ModelName}] Pulling model {Percent}% - {Status}", modelName, status.Percent, status.Status);
                    }
                }
                _logger.LogInformation("Model {ModelName} pull stream completed.", modelName);

                // Re-check if model exists after pull attempt completion
                var modelsAfterPull = await ollama.ListLocalModelsAsync();
                 if (!modelsAfterPull.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase))) {
                     _logger.LogError("Model {ModelName} still not found after pull attempt.", modelName);
                     return false; // Indicate failure
                 }
                 _logger.LogInformation("Model {ModelName} confirmed present after pull.", modelName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking or pulling Ollama model {ModelName}", modelName);
                return false;
            }
        }

        // GetChatHistory, IsSameSender, AddMessageToHistory, MapToOllamaMessage are removed as OllamaSharp.Chat handles history internally.

        // --- Main Execution Logic ---
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel)
        {
            modelName = modelName ?? Env.OllamaModelName;
            if (string.IsNullOrWhiteSpace(modelName)) {
                 _logger.LogError("Ollama model name is not configured.");
                 yield return "Error: Ollama model name is not configured.";
                 yield break;
            }

            HttpClient httpClient = _httpClientFactory?.CreateClient("OllamaClient") ?? new HttpClient(); 
            httpClient.BaseAddress = new Uri(channel.Gateway);
            var ollama = new OllamaApiClient(httpClient, modelName);

            if (!await CheckAndPullModelAsync(ollama, modelName)) {
                 yield return $"Error: Could not check or pull Ollama model '{modelName}'.";
                 yield break;
            }
            ollama.SelectedModel = modelName;

            // NOTE: History context is limited compared to OpenAIService as OllamaSharp.Chat manages it based on this initial prompt + SendAsync calls.
            var systemPrompt = $"你的名字是 {BotName}，你是一个AI助手。现在时间是：{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")}。当前对话的群聊ID是:{ChatId}。\n\n" + // Provide ChatId here for context if needed, though tools get it internally.
                               $"你的核心任务是协助用户。为此，你可以调用外部工具。以下是你当前可以使用的工具列表和它们的描述：\n\n" +
                               $"{_availableToolsPromptPart}\n\n" +
                               $"如果你判断需要使用上述列表中的某个工具，你的回复必须严格遵循以下XML格式，并且不包含任何其他文本（不要在XML前后添加任何说明或聊天内容）：\n" +
                               $"<tool_name>...</tool_name> 或 <tool name=\"tool_name\">...</tool>\n" +
                               $"(请将 'tool_name' 和参数替换为所选工具的实际名称和值。确保XML格式正确无误。)\n\n" +
                               "重要提示：如果你调用一个工具（特别是搜索类工具）后没有找到你需要的信息，或者结果不理想，你可以尝试以下操作：\n" +
                               "1. 修改你的查询参数（例如，使用更宽泛或更具体的关键词，尝试不同的搜索选项等），然后再次调用同一个工具。\n" +
                               "2. 如果多次尝试仍不理想，或者你认为其他工具可能更合适，可以尝试调用其他可用工具。\n" +
                               "3. 在进行多次尝试时，建议在思考过程中记录并调整你的策略。\n" +
                               "如果你认为已经获得了足够的信息，或者不需要再使用工具，请继续下一步。\n\n" +
                               $"在决定是否使用工具时，请仔细分析用户的请求。如果不需要工具，或者工具执行完毕后，请直接以自然语言回复用户。\n" +
                               $"当你直接回复时，请直接输出内容，不要模仿历史消息的格式。";

            // Instantiate the Chat helper with the system prompt
            var chat = new OllamaSharp.Chat(ollama, systemPrompt);

            ChatContextProvider.SetCurrentChatId(ChatId);
            try
            {
                string nextMessageToSend = message.Content; // Start with the user's current message
                int maxToolCycles = 5;

                for (int cycle = 0; cycle < maxToolCycles; cycle++)
                {
                    var llmResponseAccumulator = new StringBuilder();
                    
                    // Use the Chat helper's SendAsync method
                    await foreach (var token in chat.SendAsync(nextMessageToSend))
                    {
                        llmResponseAccumulator.Append(token);
                        // yield return token; // Optionally yield intermediate tokens
                    }
                    string llmFullResponse = llmResponseAccumulator.ToString().Trim();
                    _logger.LogDebug("LLM raw response (Cycle {Cycle}): {Response}", cycle + 1, llmFullResponse);

                    // Note: The 'chat' object internally stores history. We don't manage ollamaMessages list here.

                    if (McpToolHelper.TryParseToolCall(llmFullResponse, out string parsedToolName, out Dictionary<string, string> toolArguments))
                    {
                        // Use Newtonsoft.Json for logging arguments
                        _logger.LogInformation($"LLM requested tool: {parsedToolName} with arguments: {JsonConvert.SerializeObject(toolArguments)}");

                        try
                        {
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments);
                            string toolResultString = ConvertToolResultToString(toolResultObject);

                            _logger.LogInformation($"Tool {parsedToolName} executed. Result: {toolResultString}");
                            // Prepare the feedback for the *next* SendAsync call
                            nextMessageToSend = $"[Executed Tool '{parsedToolName}'. Result: {toolResultString}]"; 
                            _logger.LogInformation($"Prepared feedback for next LLM call: {nextMessageToSend}");
                            // Continue the loop - the next chat.SendAsync will send this feedback
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error executing tool {parsedToolName}.");
                            string errorMessage = $"Error executing tool {parsedToolName}: {ex.Message}.";
                            // Prepare the error feedback for the *next* SendAsync call
                            nextMessageToSend = $"[Tool '{parsedToolName}' Execution Failed. Error: {errorMessage}]";
                            _logger.LogInformation($"Prepared error feedback for next LLM call: {nextMessageToSend}");
                            // Continue the loop - the next chat.SendAsync will send this error feedback
                        }
                    }
                    else
                    {
                        // Not a tool call, this is the final answer.
                        if (!string.IsNullOrWhiteSpace(llmFullResponse)) {
                             yield return llmFullResponse;
                        } else {
                             _logger.LogWarning("LLM returned empty final response for ChatId {ChatId}.", ChatId);
                        }
                        yield break; // Exit loop and method
                    }
                }

                // If loop finishes due to maxToolCycles
                _logger.LogWarning("Max tool call cycles reached for chat {ChatId}.", ChatId);
                yield return "I seem to be stuck in a loop trying to use tools. Please try rephrasing your request or check tool definitions.";
            }
            finally
            {
                ChatContextProvider.Clear();
            }
        }

         // Helper to convert tool result to string (kept as it's independent of history)
         private string ConvertToolResultToString(object toolResultObject) {
             if (toolResultObject == null) {
                 return "Tool executed successfully with no return value.";
             } else if (toolResultObject is string s) {
                 return s;
             } else {
                 // Use Newtonsoft.Json for serializing result
                 return JsonConvert.SerializeObject(toolResultObject);
             }
         }
         
         // Add SetModel/GetModel/NeedReply if required for OllamaService parity and feasible with OllamaSharp.Chat helper
         // Note: Implementing these might be difficult if OllamaSharp.Chat manages state internally.
    }
}

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
using System.Text.RegularExpressions; // Added for Regex
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json; // Using Newtonsoft
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
// using OpenAIChat = OpenAI.Chat; // No longer needed as history helpers are removed

namespace TelegramSearchBot.Service.AI.LLM
{
    // Not inheriting from BaseLlmService for now due to API/history handling differences
    public class OllamaService : IService, ILLMService 
    {
        public string ServiceName => "OllamaService";

        private readonly ILogger<OllamaService> _logger;
        private readonly DataDbContext _dbContext; 
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _availableToolsPromptPart;
        public string BotName { get; set; }

        // Constructor requires dependencies needed directly by this class
        public OllamaService(
            DataDbContext context, 
            ILogger<OllamaService> logger, 
            IServiceProvider serviceProvider, 
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _dbContext = context; 
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;

            // Initialize McpToolHelper (still needed)
            McpToolHelper.Initialize(_serviceProvider, _logger);

            // Register tools and generate the prompt part once (still needed)
            _availableToolsPromptPart = McpToolHelper.RegisterToolsAndGetPromptString(Assembly.GetExecutingAssembly());
            if (string.IsNullOrWhiteSpace(_availableToolsPromptPart))
            {
                _availableToolsPromptPart = "<!-- No tools are currently available. -->";
            }
        }

        // --- Helper methods specific to this service ---

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
                await foreach (var status in ollama.PullModelAsync(modelName, System.Threading.CancellationToken.None))
                {
                    if (status != null) {
                         // Adjust property names (Percent, Status) if they differ in your OllamaSharp version
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

        // --- Main Execution Logic (Using OllamaSharp.Chat helper) ---
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel)
        {
            modelName = modelName ?? Env.OllamaModelName;
            if (string.IsNullOrWhiteSpace(modelName)) {
                 _logger.LogError("{ServiceName}: Model name is not configured.", ServiceName);
                 yield return $"Error: {ServiceName} model name is not configured.";
                 yield break;
            }
             if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway)) {
                 _logger.LogError("{ServiceName}: Channel or Gateway is not configured.", ServiceName);
                 yield return $"Error: {ServiceName} channel/gateway is not configured.";
                 yield break;
            }

            // --- Client and Model Setup ---
            HttpClient httpClient = _httpClientFactory?.CreateClient("OllamaClient") ?? new HttpClient(); 
            httpClient.BaseAddress = new Uri(channel.Gateway);
            var ollama = new OllamaApiClient(httpClient, modelName);

            if (!await CheckAndPullModelAsync(ollama, modelName)) {
                 yield return $"Error: Could not check or pull Ollama model '{modelName}'.";
                 yield break;
            }
            ollama.SelectedModel = modelName;

            // --- History and Prompt Setup ---
            // NOTE: History context is limited as OllamaSharp.Chat manages it based on this initial prompt + SendAsync calls.
            // We are NOT using the GetChatHistory method here.
            var systemPrompt = $"你的名字是 {BotName}，你是一个AI助手。现在时间是：{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")}。当前对话的群聊ID是:{ChatId}。\n\n" + 
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
                    
                    // --- Call LLM using OllamaSharp.Chat helper ---
                    _logger.LogDebug("Sending to Ollama (Cycle {Cycle}): {Message}", cycle + 1, nextMessageToSend);
                    await foreach (var token in chat.SendAsync(nextMessageToSend))
                    {
                        llmResponseAccumulator.Append(token);
                        // yield return token; // Optionally yield intermediate tokens for faster perceived response
                    }
                    string llmFullResponse = llmResponseAccumulator.ToString().Trim();
                    _logger.LogDebug("LLM raw response (Cycle {Cycle}): {Response}", cycle + 1, llmFullResponse);

                    // --- Preprocess response: Remove thinking tags ---
                    string cleanedResponse = Regex.Replace(
                        llmFullResponse, 
                        @"<think>.*?</think>", 
                        "", 
                        RegexOptions.Singleline | RegexOptions.IgnoreCase
                    ).Trim();

                    if (llmFullResponse.Length != cleanedResponse.Length) {
                         _logger.LogDebug("LLM cleaned response (Cycle {Cycle}): {Response}", cycle + 1, cleanedResponse);
                    }
                    
                    // Note: The 'chat' object internally stores history based on SendAsync calls.

                    // --- Tool Handling (using cleanedResponse) ---
                    if (McpToolHelper.TryParseToolCall(cleanedResponse, out string parsedToolName, out Dictionary<string, string> toolArguments))
                    {
                        _logger.LogInformation("{ServiceName}: LLM requested tool: {ToolName} with arguments: {Arguments}", ServiceName, parsedToolName, JsonConvert.SerializeObject(toolArguments));
                        
                        string toolResultString;
                        bool isError = false;
                        try
                        {
                            // Execute Tool
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments);
                            // Use helper for conversion (defined below)
                            toolResultString = ConvertToolResultToString(toolResultObject); 
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result: {Result}", ServiceName, parsedToolName, toolResultString);
                        }
                        catch (Exception ex)
                        {
                            isError = true;
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, parsedToolName);
                            toolResultString = $"Error executing tool {parsedToolName}: {ex.Message}.";
                        }
                        
                        // Prepare the feedback for the *next* SendAsync call
                        string feedbackPrefix = isError ? $"[Tool '{parsedToolName}' Execution Failed. Error: " : $"[Executed Tool '{parsedToolName}'. Result: ";
                        nextMessageToSend = $"{feedbackPrefix}{toolResultString}]"; 
                        _logger.LogInformation("Prepared feedback for next LLM call: {Feedback}", nextMessageToSend);
                        // Continue the loop - the next chat.SendAsync will send this feedback
                    }
                    else
                    {
                        // Not a tool call, yield the cleaned response as the final answer.
                        if (!string.IsNullOrWhiteSpace(cleanedResponse)) {
                             yield return cleanedResponse;
                        } else {
                             // Log if the original response wasn't empty but the cleaned one is (meaning it was only thinking tags)
                             if (!string.IsNullOrWhiteSpace(llmFullResponse)) {
                                 _logger.LogWarning("{ServiceName}: LLM response contained only thinking tags for ChatId {ChatId}.", ServiceName, ChatId);
                             } else {
                                 _logger.LogWarning("{ServiceName}: LLM returned empty final response for ChatId {ChatId}.", ServiceName, ChatId);
                             }
                             // Yield nothing if the cleaned response is empty
                        }
                        yield break; // Exit loop and method
                    }
                }

                // If loop finishes due to maxToolCycles
                _logger.LogWarning("{ServiceName}: Max tool call cycles reached for chat {ChatId}.", ServiceName, ChatId);
                yield return "I seem to be stuck in a loop trying to use tools. Please try rephrasing your request or check tool definitions.";
            }
            finally
            {
                ChatContextProvider.Clear(); 
            }
        }

         // Helper to convert tool result to string 
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
    }
}

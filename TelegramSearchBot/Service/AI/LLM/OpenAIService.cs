using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http; // Added for IHttpClientFactory
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Service.Common; 
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using Newtonsoft.Json; 
// Using alias for the common internal ChatMessage format
using CommonChat = OpenAI.Chat; 

namespace TelegramSearchBot.Service.AI.LLM 
{
    // Inherit from generic base class, specifying OpenAI.Chat.ChatMessage as the provider message type
    public class OpenAIService : BaseLlmService<CommonChat.ChatMessage>, ILLMService // Base class already implements IService
    {
        // ServiceName is now abstract in base class
        public override string ServiceName => "OpenAIService";

        // Constructor accepting dependencies and passing them to the base class
        // Added IHttpClientFactory to match base constructor signature
        public OpenAIService(
            DataDbContext context, 
            ILogger<OpenAIService> logger, // Use specific logger type
            IServiceProvider serviceProvider,
            IHttpClientFactory httpClientFactory) 
            : base(logger, context, serviceProvider, httpClientFactory) // Pass dependencies to base
        {
            // Base constructor handles McpToolHelper initialization and tool string generation
        }

        // --- Methods specific to OpenAI implementation ---
        // These were previously overriding abstract methods, now just regular protected methods

        protected string GetSystemPrompt(long chatId) // Removed 'override'
        {
            // Construct the system prompt using the tool list from the base class
             return $"你的名字是 {BotName}，你是一个AI助手。现在时间是：{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")}。群聊Id为:{chatId}\n\n" +
                    $"你正在参与一个群聊。历史消息的格式为：[时间] 用户名 (可选回复对象)：内容。请仔细理解上下文。\n\n" +
                    $"你的核心任务是协助用户。为此，你可以调用外部工具。以下是你当前可以使用的工具列表和它们的描述：\n\n" +
                    $"{_availableToolsPromptPart}\n\n" + // Use field from base class
                    $"如果你判断需要使用上述列表中的某个工具，你的回复必须严格遵循以下XML格式，并且不包含任何其他文本（不要在XML前后添加任何说明或聊天内容）：\n" +
                    $"<tool_name>\n" + 
                    $"  <parameter1_name>value1</parameter1_name>\n" +
                    $"  <parameter2_name>value2</parameter2_name>\n" +
                    $"  ...\n" +
                    $"</tool_name>\n" +
                    $"或者\n" +
                    $"<tool name=\"tool_name\">\n" +
                    $"  <parameters>\n" +
                    $"    <parameter1_name>value1</parameter1_name>\n" +
                    $"  </parameters>\n" +
                    $"</tool>\n" +
                    $"(请将 'tool_name' 和参数替换为所选工具的实际名称和值。确保XML格式正确无误。)\n\n" +
                    "重要提示：如果你调用一个工具（特别是搜索类工具）后没有找到你需要的信息，或者结果不理想，你可以尝试以下操作：\n" +
                    "1. 修改你的查询参数（例如，使用更宽泛或更具体的关键词，尝试不同的搜索选项等），然后再次调用同一个工具。\n" +
                    "2. 如果多次尝试仍不理想，或者你认为其他工具可能更合适，可以尝试调用其他可用工具。\n" +
                    "3. 在进行多次尝试时，建议在思考过程中记录并调整你的策略。\n" +
                    "如果你认为已经获得了足够的信息，或者不需要再使用工具，请继续下一步。\n\n" +
                    $"在决定是否使用工具时，请仔细分析用户的请求。如果不需要工具，或者工具执行完毕后，请直接以自然语言回复用户。\n" +
                    $"当你直接回复时，请直接输出内容，不要模仿历史消息的格式。";
        }

        // Mapping is trivial since common format IS the provider format for OpenAI
        protected List<CommonChat.ChatMessage> MapHistoryToProviderFormat(List<CommonChat.ChatMessage> commonHistory) // Removed 'override'
        {
            // In this case, the provider format is the same as the common format
            return commonHistory;
        }

        // Add Assistant response to the history list (OpenAI format)
        protected void AddAssistantResponseToHistory(List<CommonChat.ChatMessage> providerHistory, string llmFullResponse) // Removed 'override'
        {
             providerHistory.Add(new CommonChat.AssistantChatMessage(llmFullResponse));
        }
        
        // Add Tool feedback to the history list (OpenAI format, using User role workaround)
        protected void AddToolFeedbackToHistory(List<CommonChat.ChatMessage> providerHistory, string toolName, string toolResult, bool isError) // Removed 'override'
        {
            string feedbackPrefix = isError ? $"[Tool '{toolName}' Execution Failed. Error: " : $"[Executed Tool '{toolName}'. Result: ";
            string feedback = $"{feedbackPrefix}{toolResult}]";
            providerHistory.Add(new CommonChat.UserChatMessage(feedback)); // Using User role as requested
             _logger.LogInformation("Added UserChatMessage to history for LLM: {Feedback}", feedback);
        }

        // Implement the main execution logic, reusing base class helpers where possible
        public override async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel)
        {
             if (string.IsNullOrWhiteSpace(modelName)) modelName = Env.OpenAIModelName; // Use default if needed

             if (string.IsNullOrWhiteSpace(modelName)) {
                 _logger.LogError("{ServiceName}: Model name is not configured.", ServiceName);
                 yield return $"Error: {ServiceName} model name is not configured.";
                 yield break;
             }
             if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway) || string.IsNullOrWhiteSpace(channel.ApiKey)) {
                 _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured.", ServiceName);
                 yield return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
                 yield break;
             }

            // --- History and Prompt Setup ---
            string systemPrompt = GetSystemPrompt(ChatId);
            // Use base class GetChatHistory
            List<CommonChat.ChatMessage> commonHistory = new List<CommonChat.ChatMessage>() { new CommonChat.SystemChatMessage(systemPrompt) };
            commonHistory = await base.GetChatHistory(ChatId, commonHistory, message); 
            // Mapping is direct for OpenAI
            var providerHistory = MapHistoryToProviderFormat(commonHistory); 

            // --- Client Setup ---
            // TODO: Consider if client needs recreation per call or can be reused/cached based on channel
             var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(channel.Gateway) };
             var chatClient = new ChatClient(model: modelName, credential: new(channel.ApiKey), clientOptions);


            ChatContextProvider.SetCurrentChatId(ChatId); 
            try
            {
                int maxToolCycles = 5;
                for (int cycle = 0; cycle < maxToolCycles; cycle++)
                {
                    var llmResponseAccumulator = new StringBuilder();
                    
                    // --- Call LLM ---
                    // Use the specific OpenAI client call
                    await foreach (var update in chatClient.CompleteChatStreamingAsync(providerHistory))
                    {
                        // Note: OpenAI SDK might structure updates differently. Adapt as needed.
                        // Assuming update.ContentUpdate gives text parts.
                        foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>())
                        {
                             // Check if updatePart itself is null or its Text property
                             if (updatePart?.Text != null) {
                                 llmResponseAccumulator.Append(updatePart.Text);
                             }
                        }
                    }
                    string llmFullResponse = llmResponseAccumulator.ToString().Trim();
                    _logger.LogDebug("{ServiceName} raw response (Cycle {Cycle}): {Response}", ServiceName, cycle + 1, llmFullResponse);

                    if (string.IsNullOrWhiteSpace(llmFullResponse) && cycle < maxToolCycles -1) {
                        _logger.LogWarning("{ServiceName}: LLM returned empty response during tool cycle {Cycle}.", ServiceName, cycle + 1);
                        // Handle empty response? Maybe break or add specific feedback? Add assistant message anyway.
                    }

                    // Add Assistant response to history
                    AddAssistantResponseToHistory(providerHistory, llmFullResponse);

                    // --- Tool Handling ---
                    if (McpToolHelper.TryParseToolCall(llmFullResponse, out string parsedToolName, out Dictionary<string, string> toolArguments))
                    {
                        _logger.LogInformation("{ServiceName}: LLM requested tool: {ToolName} with arguments: {Arguments}", ServiceName, parsedToolName, JsonConvert.SerializeObject(toolArguments));
                        
                        string toolResultString;
                        bool isError = false;
                        try
                        {
                            // Execute Tool (uses McpToolHelper from base class context)
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments);
                            // Use base class helper for conversion
                            toolResultString = base.ConvertToolResultToString(toolResultObject); 
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result: {Result}", ServiceName, parsedToolName, toolResultString);
                        }
                        catch (Exception ex)
                        {
                            isError = true;
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, parsedToolName);
                            toolResultString = $"Error executing tool {parsedToolName}: {ex.Message}.";
                        }
                        
                        // Add tool feedback to history
                        AddToolFeedbackToHistory(providerHistory, parsedToolName, toolResultString, isError); 
                        // Continue loop for LLM to process feedback
                    }
                    else
                    {
                        // Not a tool call, this is the final answer.
                        if (!string.IsNullOrWhiteSpace(llmFullResponse)) {
                             yield return llmFullResponse;
                        } else {
                             _logger.LogWarning("{ServiceName}: LLM returned empty final response for ChatId {ChatId}.", ServiceName, ChatId);
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

        // --- Methods not needed as they are in BaseLlmService ---
        // GetChatHistory
        // IsSameSender
        // AddMessageToHistory (internal helper used by GetChatHistory in base)
        // ConvertToolResultToString

        // --- Methods specific to OpenAIService (if any) or potentially moved to base ---
        // NeedReply, SetModel, GetModel - Keep them here for now if specific to OpenAI workflow or not abstracted yet.
         public async Task<bool> NeedReply(string InputToken, long ChatId)
        {
            // This method might need refactoring if it uses history differently now
            // For now, keep original logic but ensure GetChatHistory is called correctly if needed
            // It currently creates its own history list, which might be inefficient.
            // TODO: Refactor NeedReply to potentially reuse history building logic or integrate better.
            
            var prompt = $"你是一个判断助手，只负责判断一段消息是否为提问。\r\n判断标准：\r\n1. 如果消息是问题（无论是直接问句还是隐含的提问意图），返回“是”。\r\n2. 如果消息不是问题（陈述、感叹、命令、闲聊等），返回“否”。\r\n重要：只回答“是”或“否”，不要输出其他内容。";

            // Temporarily create history for this check
            List<CommonChat.ChatMessage> checkHistory = new List<CommonChat.ChatMessage>() { new CommonChat.SystemChatMessage(prompt) };
            // Use base GetChatHistory, passing null for inputToken as we add the current one separately
            checkHistory = await base.GetChatHistory(ChatId, checkHistory, null); 
            checkHistory.Add(new CommonChat.UserChatMessage($"消息：{InputToken}")); 

            // Use a temporary client for this check
            // TODO: Optimize client usage
             var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(Env.OpenAIBaseURL) }; // Assuming a default endpoint for this check
             var chat = new ChatClient(model: Env.OpenAIModelName, credential: new(Env.OpenAIApiKey), clientOptions); // Assuming default model/key for this check

            var str = new StringBuilder();
            // Use the mapped history if the SDK requires it, but CompleteChatStreamingAsync takes List<ChatMessage>
            await foreach (var update in chat.CompleteChatStreamingAsync(checkHistory)) 
            {
                foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>())
                {
                     if (updatePart?.Text != null) str.Append(updatePart.Text);
                }
            }
            if (str.Length < 2 && str.ToString().Contains('是'))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public async Task<(string, string)> SetModel(string ModelName, long ChatId)
        {
            var GroupSetting = await _dbContext.GroupSettings
                                .Where(s => s.GroupId == ChatId)
                                .FirstOrDefaultAsync();
            var CurrentModelName = GroupSetting?.LLMModelName;
            if (GroupSetting is null)
            {
                await _dbContext.AddAsync(new GroupSettings() { GroupId = ChatId, LLMModelName = ModelName });
            }
            else
            {
                GroupSetting.LLMModelName = ModelName;
            }
            await _dbContext.SaveChangesAsync();
            return (CurrentModelName ?? "Default", ModelName); // Return default if null
        }
        public async Task<string> GetModel(long ChatId)
        {
            var GroupSetting = await _dbContext.GroupSettings.AsNoTracking()
                                      .Where(s => s.GroupId == ChatId)
                                      .FirstOrDefaultAsync();
            var ModelName = GroupSetting?.LLMModelName;
            return ModelName; // Returns null if not set
        }

        // CheckIfExists seems Ollama specific, remove from here
        // public bool CheckIfExists(IEnumerable<OllamaSharp.Models.Model> models) { ... } 
    }
}

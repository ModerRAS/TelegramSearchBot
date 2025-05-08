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
    // Standalone implementation, not inheriting from BaseLlmService
    public class OpenAIService : IService, ILLMService 
    {
        public string ServiceName => "OpenAIService";

        private readonly ILogger<OpenAIService> _logger;
        public string BotName { get; set; }
        private DataDbContext _dbContext;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory; // Keep for consistency, though not used directly here
        private readonly string _availableToolsPromptPart;

        public OpenAIService(
            DataDbContext context, 
            ILogger<OpenAIService> logger, 
            IServiceProvider serviceProvider,
            IHttpClientFactory httpClientFactory) // Added httpClientFactory
        {
            _logger = logger;
            _dbContext = context;
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory; // Store it

            McpToolHelper.Initialize(_serviceProvider, _logger); 

            _availableToolsPromptPart = McpToolHelper.RegisterToolsAndGetPromptString(Assembly.GetExecutingAssembly());
            if (string.IsNullOrWhiteSpace(_availableToolsPromptPart))
            {
                _availableToolsPromptPart = "<!-- No tools are currently available. -->";
            }
        }

        // --- Helper Methods (Defined locally again) ---

        public bool IsSameSender(Model.Data.Message message1, Model.Data.Message message2)
        {
            if (message1 == null || message2 == null) return false; 
            bool msg1IsUser = message1.FromUserId != Env.BotId;
            bool msg2IsUser = message2.FromUserId != Env.BotId;
            return msg1IsUser == msg2IsUser; 
        }

        private void AddMessageToHistory(List<ChatMessage> ChatHistory, long fromUserId, string content)
        {
             if (string.IsNullOrWhiteSpace(content)) return;
             content = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\n{3,}", "\n\n");

            if (fromUserId == Env.BotId)
            {
                ChatHistory.Add(new AssistantChatMessage(content.Trim()));
            }
            else
            {
                ChatHistory.Add(new UserChatMessage(content.Trim()));
            }
        }

        public async Task<List<ChatMessage>> GetChatHistory(long ChatId, List<ChatMessage> ChatHistory, Model.Data.Message InputToken)
        {
            var Messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == ChatId && m.DateTime > DateTime.UtcNow.AddHours(-1))
                            .OrderBy(m => m.DateTime) 
                            .ToListAsync();
            if (Messages.Count < 10)
            {
                Messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == ChatId)
                            .OrderByDescending(m => m.DateTime)
                            .Take(10)
                            .OrderBy(m => m.DateTime) 
                            .ToListAsync();
            }
            if (InputToken != null)
            {
                Messages.Add(InputToken);
            }
            _logger.LogInformation($"OpenAI GetChatHistory: Found {Messages.Count} messages for ChatId {ChatId}.");

            var str = new StringBuilder();
            Model.Data.Message previous = null;
            var userCache = new Dictionary<long, UserData>(); 

            foreach (var message in Messages)
            {
                 if (previous == null && !ChatHistory.Any(ch => ch is UserChatMessage || ch is AssistantChatMessage) && message.FromUserId.Equals(Env.BotId))
                 {
                     previous = message; 
                     continue;
                 }

                if (previous != null && !IsSameSender(previous, message))
                {
                    AddMessageToHistory(ChatHistory, previous.FromUserId, str.ToString());
                    str.Clear();
                }

                str.Append($"[{message.DateTime.ToString("yyyy-MM-dd HH:mm:ss zzz")}]");
                if (message.FromUserId != 0)
                {
                     if (!userCache.TryGetValue(message.FromUserId, out var fromUser))
                    {
                        fromUser = await _dbContext.UserData.AsNoTracking().FirstOrDefaultAsync(u => u.Id == message.FromUserId);
                        if (fromUser != null) userCache[message.FromUserId] = fromUser;
                    }
                    str.Append(fromUser != null ? $"{fromUser.FirstName} {fromUser.LastName}".Trim() : $"User({message.FromUserId})");
                }
                else { str.Append("System/Unknown"); }

                if (message.ReplyToMessageId != 0)
                {
                    str.Append('（');
                     // Simplified reply info
                    str.Append($"Reply to msg {message.ReplyToMessageId}"); 
                    str.Append('）');
                }
                str.Append('：').Append(message.Content).Append("\n"); 

                previous = message; 
            }
            if (previous != null && str.Length > 0)
            {
                AddMessageToHistory(ChatHistory, previous.FromUserId, str.ToString());
            }
            return ChatHistory;
        }

         private string ConvertToolResultToString(object toolResultObject) {
             if (toolResultObject == null) {
                 return "Tool executed successfully with no return value.";
             } else if (toolResultObject is string s) {
                 return s;
             } else {
                 return JsonConvert.SerializeObject(toolResultObject); 
             }
         }

        // --- Main Execution Logic ---
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel)
        {
             if (string.IsNullOrWhiteSpace(modelName)) modelName = Env.OpenAIModelName; 

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
            // Use helper method to format the prompt
            string systemPrompt = McpToolHelper.FormatSystemPrompt(BotName, ChatId, _availableToolsPromptPart);
            List<ChatMessage> providerHistory = new List<ChatMessage>() { new SystemChatMessage(systemPrompt) };
            providerHistory = await GetChatHistory(ChatId, providerHistory, message); // Use local GetChatHistory

            // --- Client Setup ---
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
                    await foreach (var update in chatClient.CompleteChatStreamingAsync(providerHistory))
                    {
                        foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>())
                        {
                             if (updatePart?.Text != null) {
                                 llmResponseAccumulator.Append(updatePart.Text);
                             }
                        }
                    }
                    string llmFullResponse = llmResponseAccumulator.ToString().Trim();
                    _logger.LogDebug("{ServiceName} raw response (Cycle {Cycle}): {Response}", ServiceName, cycle + 1, llmFullResponse);

                    // --- Preprocess response using McpToolHelper ---
                    string cleanedResponse = McpToolHelper.CleanLlmResponse(llmFullResponse);
                    if (llmFullResponse.Length != cleanedResponse.Length) {
                         _logger.LogDebug("{ServiceName} cleaned response (Cycle {Cycle}): {Response}", ServiceName, cycle + 1, cleanedResponse);
                    }

                    if (string.IsNullOrWhiteSpace(cleanedResponse) && cycle < maxToolCycles -1) {
                         if (!string.IsNullOrWhiteSpace(llmFullResponse)) {
                             _logger.LogWarning("{ServiceName}: LLM response contained only thinking tags during tool cycle {Cycle}.", ServiceName, cycle + 1);
                         } else {
                             _logger.LogWarning("{ServiceName}: LLM returned empty response during tool cycle {Cycle}.", ServiceName, cycle + 1);
                         }
                    }
                    
                    // Add Assistant response (raw) to history 
                    if (!string.IsNullOrWhiteSpace(llmFullResponse)) {
                         providerHistory.Add(new AssistantChatMessage(llmFullResponse));
                     } else {
                          _logger.LogWarning("{ServiceName}: Attempted to add empty assistant response to history.", ServiceName);
                     }

                    // --- Tool Handling (using cleanedResponse) ---
                    if (McpToolHelper.TryParseToolCall(cleanedResponse, out string parsedToolName, out Dictionary<string, string> toolArguments))
                    {
                        _logger.LogInformation("{ServiceName}: LLM requested tool: {ToolName} with arguments: {Arguments}", ServiceName, parsedToolName, JsonConvert.SerializeObject(toolArguments));
                        
                        string toolResultString;
                        bool isError = false;
                        try
                        {
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments);
                            toolResultString = ConvertToolResultToString(toolResultObject); // Use local helper
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result: {Result}", ServiceName, parsedToolName, toolResultString);
                        }
                        catch (Exception ex)
                        {
                            isError = true;
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, parsedToolName);
                            toolResultString = $"Error executing tool {parsedToolName}: {ex.Message}.";
                        }
                        
                        // Add tool feedback to history (using User role workaround)
                        string feedbackPrefix = isError ? $"[Tool '{parsedToolName}' Execution Failed. Error: " : $"[Executed Tool '{parsedToolName}'. Result: ";
                        string feedback = $"{feedbackPrefix}{toolResultString}]";
                        providerHistory.Add(new UserChatMessage(feedback)); 
                        _logger.LogInformation("Added UserChatMessage to history for LLM: {Feedback}", feedback);
                        // Continue loop 
                    }
                    else
                    {
                        // Not a tool call, yield the cleaned response
                        if (!string.IsNullOrWhiteSpace(cleanedResponse)) {
                             yield return cleanedResponse;
                        } else {
                             _logger.LogWarning("{ServiceName}: LLM returned empty final response after cleaning for ChatId {ChatId}.", ServiceName, ChatId);
                        }
                        yield break; 
                    }
                }

                _logger.LogWarning("{ServiceName}: Max tool call cycles reached for chat {ChatId}.", ServiceName, ChatId);
                yield return "I seem to be stuck in a loop trying to use tools. Please try rephrasing your request or check tool definitions.";
            }
            finally
            {
                ChatContextProvider.Clear(); 
            }
        }

        // --- Methods specific to OpenAIService ---
         public async Task<bool> NeedReply(string InputToken, long ChatId)
        {
            var prompt = $"你是一个判断助手，只负责判断一段消息是否为提问。\r\n判断标准：\r\n1. 如果消息是问题（无论是直接问句还是隐含的提问意图），返回“是”。\r\n2. 如果消息不是问题（陈述、感叹、命令、闲聊等），返回“否”。\r\n重要：只回答“是”或“否”，不要输出其他内容。";

            List<ChatMessage> checkHistory = new List<ChatMessage>() { new SystemChatMessage(prompt) };
            // Use local GetChatHistory
            checkHistory = await GetChatHistory(ChatId, checkHistory, null); 
            checkHistory.Add(new UserChatMessage($"消息：{InputToken}")); 

             var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(Env.OpenAIBaseURL) }; 
             var chat = new ChatClient(model: Env.OpenAIModelName, credential: new(Env.OpenAIApiKey), clientOptions); 

            var str = new StringBuilder();
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
            return (CurrentModelName ?? "Default", ModelName); 
        }
        public async Task<string> GetModel(long ChatId)
        {
            var GroupSetting = await _dbContext.GroupSettings.AsNoTracking()
                                      .Where(s => s.GroupId == ChatId)
                                      .FirstOrDefaultAsync();
            var ModelName = GroupSetting?.LLMModelName;
            return ModelName; 
        }
    }
}

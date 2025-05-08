using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading; 
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
using Newtonsoft.Json; 
// Using alias for the common internal ChatMessage format
using CommonChat = OpenAI.Chat; 

namespace TelegramSearchBot.Service.AI.LLM
{
    public abstract class BaseLlmService<TProviderMessage> : IService, ILLMService where TProviderMessage : class 
    {
        public abstract string ServiceName { get; } 

        protected readonly ILogger _logger; 
        protected readonly DataDbContext _dbContext;
        protected readonly IServiceProvider _serviceProvider;
        protected readonly IHttpClientFactory _httpClientFactory;
        protected readonly string _availableToolsPromptPart;
        public string BotName { get; set; }

        protected BaseLlmService(
            ILogger logger, 
            DataDbContext context, 
            IServiceProvider serviceProvider, 
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _dbContext = context;
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;

            McpToolHelper.Initialize(_serviceProvider, _logger);

            _availableToolsPromptPart = McpToolHelper.RegisterToolsAndGetPromptString(Assembly.GetExecutingAssembly());
            if (string.IsNullOrWhiteSpace(_availableToolsPromptPart))
            {
                _availableToolsPromptPart = "<!-- No tools are currently available. -->";
            }
             _logger.LogInformation("BaseLlmService initialized. Found tools: {HasTools}", !string.IsNullOrWhiteSpace(_availableToolsPromptPart) && !_availableToolsPromptPart.Contains("No tools"));
        }

        // --- Common Helper Methods ---

        protected bool IsSameSender(Model.Data.Message message1, Model.Data.Message message2)
        {
            if (message1 == null || message2 == null) return false;
            bool msg1IsUser = message1.FromUserId != Env.BotId;
            bool msg2IsUser = message2.FromUserId != Env.BotId;
            return msg1IsUser == msg2IsUser;
        }

        protected void AddMessageToHistory(List<CommonChat.ChatMessage> chatHistory, long fromUserId, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            content = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\n{3,}", "\n\n");

            if (fromUserId == Env.BotId)
            {
                chatHistory.Add(new CommonChat.AssistantChatMessage(content));
            }
            else
            {
                chatHistory.Add(new CommonChat.UserChatMessage(content));
            }
        }

        protected async Task<List<CommonChat.ChatMessage>> GetChatHistory(long ChatId, List<CommonChat.ChatMessage> chatHistory, Model.Data.Message inputToken)
        {
             var messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == ChatId && m.DateTime > DateTime.UtcNow.AddHours(-1))
                            .OrderBy(m => m.DateTime) 
                            .ToListAsync();
            
            if (messages.Count < 10)
            {
                 messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == ChatId)
                            .OrderByDescending(m => m.DateTime)
                            .Take(10)
                            .OrderBy(m => m.DateTime) 
                            .ToListAsync();
            }

            if (inputToken != null)
            {
                messages.Add(inputToken); 
            }
            _logger.LogInformation("GetChatHistory: Found {Count} messages for ChatId {ChatId}.", messages.Count, ChatId);

            var str = new StringBuilder();
            Model.Data.Message previous = null;
            var userCache = new Dictionary<long, UserData>(); 

            foreach (var message in messages)
            {
                 if (previous == null && !chatHistory.Any(ch => ch is CommonChat.UserChatMessage || ch is CommonChat.AssistantChatMessage) && message.FromUserId.Equals(Env.BotId))
                 {
                     previous = message; 
                     continue;
                 }

                if (previous != null && !IsSameSender(previous, message))
                {
                    AddMessageToHistory(chatHistory, previous.FromUserId, str.ToString());
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
                    str.Append($"Reply to msg {message.ReplyToMessageId}"); 
                    str.Append('）');
                }
                str.Append('：').Append(message.Content).Append("\n"); 

                previous = message; 
            }
            if (previous != null && str.Length > 0)
            {
                AddMessageToHistory(chatHistory, previous.FromUserId, str.ToString());
            }
            return chatHistory;
        }

        protected string ConvertToolResultToString(object toolResultObject) {
             if (toolResultObject == null) {
                 return "Tool executed successfully with no return value.";
             } else if (toolResultObject is string s) {
                 return s;
             } else {
                 return JsonConvert.SerializeObject(toolResultObject); 
             }
         }

        // --- Abstract Methods for Derived Classes ---

        /// <summary>
        /// Gets the provider-specific system prompt string.
        /// </summary>
        protected abstract string GetSystemPrompt(long chatId);

        /// <summary>
        /// Maps the common internal chat history format to the provider-specific format.
        /// </summary>
        protected abstract List<TProviderMessage> MapHistoryToProviderFormat(List<CommonChat.ChatMessage> commonHistory);
        
        /// <summary>
        /// Sends the request to the LLM provider and streams the response chunks.
        /// </summary>
        /// <returns>An async stream of response content chunks.</returns>
        protected abstract IAsyncEnumerable<string> StreamLlmResponseAsync(List<TProviderMessage> providerHistory, string modelName, LLMChannel channel);

        /// <summary>
        /// Adds the assistant's response message to the provider-specific history list.
        /// </summary>
        protected abstract void AddAssistantResponseToHistory(List<TProviderMessage> providerHistory, string llmFullResponse);

        /// <summary>
        /// Adds the tool feedback message (as User role workaround) to the provider-specific history list.
        /// </summary>
        protected abstract void AddToolFeedbackToHistory(List<TProviderMessage> providerHistory, string toolName, string toolResult, bool isError);


        // --- Main Execution Logic (Common Tool Loop in Base Class) ---
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel)
        {
            // Validation
             if (string.IsNullOrWhiteSpace(modelName)) {
                 _logger.LogError("{ServiceName}: Model name was not provided and no default is set.", ServiceName);
                 yield return $"Error: {ServiceName} model name is not configured.";
                 yield break;
             }
             if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway)) {
                 _logger.LogError("{ServiceName}: Channel or Gateway is not configured for model {ModelName}.", ServiceName, modelName);
                 yield return $"Error: {ServiceName} channel/gateway is not configured.";
                 yield break;
             }

            // Setup
            string systemPrompt = GetSystemPrompt(ChatId);
            List<CommonChat.ChatMessage> commonHistory = new List<CommonChat.ChatMessage>() { new CommonChat.SystemChatMessage(systemPrompt) };
            commonHistory = await GetChatHistory(ChatId, commonHistory, message); 
            var providerHistory = MapHistoryToProviderFormat(commonHistory); 

            ChatContextProvider.SetCurrentChatId(ChatId);
            try
            {
                int maxToolCycles = 5;
                for (int cycle = 0; cycle < maxToolCycles; cycle++)
                {
                    var llmResponseAccumulator = new StringBuilder();
                    
                    // Call provider-specific streaming method
                    await foreach (var chunk in StreamLlmResponseAsync(providerHistory, modelName, channel)) 
                    {
                        llmResponseAccumulator.Append(chunk);
                    }
                    string llmFullResponse = llmResponseAccumulator.ToString().Trim();
                    
                    // Clean the response
                    string cleanedResponse = McpToolHelper.CleanLlmResponse(llmFullResponse);

                     _logger.LogDebug("{ServiceName} raw response (Cycle {Cycle}): {Response}", ServiceName, cycle + 1, llmFullResponse);
                     if (llmFullResponse.Length != cleanedResponse.Length) {
                          _logger.LogDebug("{ServiceName} cleaned response (Cycle {Cycle}): {Response}", ServiceName, cycle + 1, cleanedResponse);
                     }

                    if (string.IsNullOrWhiteSpace(cleanedResponse) && cycle < maxToolCycles -1) {
                         if (!string.IsNullOrWhiteSpace(llmFullResponse)) {
                             _logger.LogWarning("{ServiceName}: LLM response contained only thinking tags during tool cycle {Cycle}.", ServiceName, cycle + 1);
                         } else {
                             _logger.LogWarning("{ServiceName}: LLM returned empty response during tool cycle {Cycle}.", ServiceName, cycle + 1);
                         }
                         // If response was only thinking tags or empty, we might need to break or send specific feedback.
                         // For now, add the raw (empty or thinking) response to history and let the loop continue/fail.
                    }

                    // Add Assistant response (raw) to provider history BEFORE checking for tool call
                    AddAssistantResponseToHistory(providerHistory, llmFullResponse); 

                    // Check cleaned response for tool call
                    if (McpToolHelper.TryParseToolCall(cleanedResponse, out string parsedToolName, out Dictionary<string, string> toolArguments))
                    {
                        _logger.LogInformation("{ServiceName}: LLM requested tool: {ToolName} with arguments: {Arguments}", ServiceName, parsedToolName, JsonConvert.SerializeObject(toolArguments));
                        
                        string toolResultString;
                        bool isError = false;
                        try
                        {
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments);
                            toolResultString = ConvertToolResultToString(toolResultObject); 
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result: {Result}", ServiceName, parsedToolName, toolResultString);
                        }
                        catch (Exception ex)
                        {
                            isError = true;
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, parsedToolName);
                            toolResultString = $"Error executing tool {parsedToolName}: {ex.Message}.";
                        }
                        
                        // Add tool feedback to provider history
                        AddToolFeedbackToHistory(providerHistory, parsedToolName, toolResultString, isError); 
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
    }
}

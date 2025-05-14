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
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
using Newtonsoft.Json; 
using TelegramSearchBot.Service.Tools; // Added for DuckDuckGoSearchResult
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
            _logger.LogInformation("BaseLlmService instance created. McpToolHelper should be initialized at application startup.");
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
             }
             // else if (toolResultObject is DuckDuckGoSearchResult ddgResult) ... (This logic is now in McpToolHelper)
             else {
                 // Delegate to McpToolHelper for consistent formatting including DuckDuckGo.
                 // This ensures BaseLlmService also uses the centralized formatter if its ConvertToolResultToString is ever called.
                 return McpToolHelper.ConvertToolResultToString(toolResultObject);
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
        protected abstract IAsyncEnumerable<string> StreamLlmResponseAsync(List<TProviderMessage> providerHistory, string modelName, LLMChannel channel, CancellationToken cancellationToken);

        /// <summary>
        /// Adds the assistant's response message to the provider-specific history list.
        /// </summary>
        protected abstract void AddAssistantResponseToHistory(List<TProviderMessage> providerHistory, string llmFullResponse);

        /// <summary>
        /// Adds the tool feedback message (as User role workaround) to the provider-specific history list.
        /// </summary>
        protected abstract void AddToolFeedbackToHistory(List<TProviderMessage> providerHistory, string toolName, string toolResult, bool isError);


        // --- Main Execution Logic (Common Tool Loop in Base Class) ---
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("{ServiceName}.ExecAsync was called, but it's not intended for direct use in the current setup as OpenAIService and OllamaService provide their own implementations.", ServiceName);

            throw new NotImplementedException($"{ServiceName}.ExecAsync in BaseLlmService is not fully implemented for direct use in the current service structure. Specific services (OpenAI, Ollama) have their own ExecAsync implementations.");
            yield return string.Empty;
        }
    }
}

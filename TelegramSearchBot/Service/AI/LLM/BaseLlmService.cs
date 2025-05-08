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
    // Make the base class generic on the provider's message type
    // Note: TProviderMessage is currently not used due to ExecAsync being abstract,
    // but kept for potential future refactoring if common loop logic is moved back here.
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

        // --- Common Helper Methods (Available for Derived Classes) ---

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
        
        // Only ExecAsync needs to be implemented by derived classes now.
        public abstract IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel);

        // Removed other abstract methods as ExecAsync is now abstract:
        // protected abstract string GetSystemPrompt(long chatId);
        // protected abstract List<TProviderMessage> MapHistoryToProviderFormat(List<CommonChat.ChatMessage> commonHistory);
        // protected abstract Task<string> GetLlmResponseAsync(List<TProviderMessage> providerHistory, string modelName, LLMChannel channel, CancellationToken cancellationToken = default);
        // protected abstract void AddAssistantResponseToHistory(List<TProviderMessage> providerHistory, string llmFullResponse);
        // protected abstract void AddToolFeedbackToHistory(List<TProviderMessage> providerHistory, string toolName, string toolResult, bool isError);
    }
}

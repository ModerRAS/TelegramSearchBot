using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using GenerativeAI;
using GenerativeAI.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM
{
    public class GeminiService : ILLMService, IService
    {
        public string ServiceName => "GeminiService";
        private readonly ILogger<GeminiService> _logger;
        private readonly DataDbContext _dbContext;
        private readonly Dictionary<long, ChatSession> _chatSessions = new();
        public string BotName { get; set; }

        public GeminiService(
            DataDbContext context,
            ILogger<GeminiService> logger)
        {
            _logger = logger;
            _dbContext = context;
            _logger.LogInformation("GeminiService instance created");
        }

        private void AddMessageToHistory(List<GenerativeAI.Types.Content> chatHistory, long fromUserId, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            content = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\n{3,}", "\n\n");

            var role = fromUserId == Env.BotId ? Roles.Model : Roles.User;
            chatHistory.Add(new Content(content, role));
        }

        public async Task<List<GenerativeAI.Types.Content>> GetChatHistory(long chatId, Message inputMessage = null)
        {
            var messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == chatId && m.DateTime > DateTime.UtcNow.AddHours(-1))
                            .OrderBy(m => m.DateTime)
                            .ToListAsync();

            if (messages.Count < 10)
            {
                messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == chatId)
                            .OrderByDescending(m => m.DateTime)
                            .Take(10)
                            .OrderBy(m => m.DateTime)
                            .ToListAsync();
            }

            if (inputMessage != null)
            {
                messages.Add(inputMessage);
            }

            var chatHistory = new List<GenerativeAI.Types.Content>();
            var str = new StringBuilder();
            Message previous = null;
            var userCache = new Dictionary<long, UserData>();

            foreach (var message in messages)
            {
                if (previous == null && !chatHistory.Any() && message.FromUserId == Env.BotId)
                {
                    previous = message;
                    continue;
                }

                if (previous != null && (previous.FromUserId == Env.BotId) != (message.FromUserId == Env.BotId))
                {
                    AddMessageToHistory(chatHistory, previous.FromUserId, str.ToString());
                    str.Clear();
                }

                str.Append($"[{message.DateTime:yyyy-MM-dd HH:mm:ss zzz}]");
                if (message.FromUserId != 0)
                {
                    if (!userCache.TryGetValue(message.FromUserId, out var fromUser))
                    {
                        fromUser = await _dbContext.UserData.AsNoTracking()
                            .FirstOrDefaultAsync(u => u.Id == message.FromUserId);
                        if (fromUser != null) userCache[message.FromUserId] = fromUser;
                    }
                    str.Append(fromUser != null ? $"{fromUser.FirstName} {fromUser.LastName}".Trim() : $"User({message.FromUserId})");
                }
                else
                {
                    str.Append("System/Unknown");
                }

                if (message.ReplyToMessageId != 0)
                {
                    str.Append($" (Reply to msg {message.ReplyToMessageId})");
                }
                str.Append(": ").Append(message.Content).Append("\n");

                previous = message;
            }

            if (previous != null && str.Length > 0)
            {
                AddMessageToHistory(chatHistory, previous.FromUserId, str.ToString());
            }

            return chatHistory;
        }

        public async IAsyncEnumerable<string> ExecAsync(
            Message message, 
            long ChatId, 
            string modelName, 
            LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(modelName)) modelName = "gemini-1.5-flash";
            if (channel == null || string.IsNullOrWhiteSpace(channel.ApiKey))
            {
                _logger.LogError("{ServiceName}: Channel or ApiKey is not configured", ServiceName);
                yield return $"Error: {ServiceName} channel/apikey is not configured";
                yield break;
            }

            var googleAI = new GoogleAi(channel.ApiKey);
            var model = googleAI.CreateGenerativeModel("models/" + modelName);
            var fullResponse = new StringBuilder();

            var history = await GetChatHistory(ChatId, message);
            if (!_chatSessions.TryGetValue(ChatId, out var chatSession))
            {
                chatSession = model.StartChat(history: history);
                _chatSessions[ChatId] = chatSession;
            }

            await foreach (var chunk in chatSession.StreamContentAsync(message.Content)) {
                fullResponse.Append(chunk.Text);
                yield return fullResponse.ToString();
            }
        }
    }
}

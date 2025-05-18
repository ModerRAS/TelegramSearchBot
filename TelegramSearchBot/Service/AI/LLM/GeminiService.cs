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
using Newtonsoft.Json;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using System.Net.Http;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM
{
    public class GeminiService : ILLMService, IService
    {
        public string ServiceName => "GeminiService";
        private readonly ILogger<GeminiService> _logger;
        private readonly DataDbContext _dbContext;
        private readonly Dictionary<long, ChatSession> _chatSessions = new();
        private readonly IHttpClientFactory _httpClientFactory;
        public string BotName { get; set; }

        public GeminiService(
            DataDbContext context,
            ILogger<GeminiService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _dbContext = context;
            _logger.LogInformation("GeminiService instance created");
            _httpClientFactory = httpClientFactory;
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

        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            if (channel.Provider.Equals(LLMProvider.Ollama)) {
                return new List<string>();
            }

            try {
                var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
                var modelsResponse = await googleAI.ListModelsAsync();
                return modelsResponse.Models.Select(m => m.Name.Replace("models/", ""));
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to list Gemini models");
                return new List<string>();
            }
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

            var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
            var model = googleAI.CreateGenerativeModel("models/" + modelName);
            var fullResponse = new StringBuilder();

            var history = await GetChatHistory(ChatId, message);
            if (!_chatSessions.TryGetValue(ChatId, out var chatSession))
            {
                chatSession = model.StartChat(history: history);
                _chatSessions[ChatId] = chatSession;
            }

            int maxToolCycles = 5;
            for (int cycle = 0; cycle < maxToolCycles; cycle++)
            {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                var currentMessageBuilder = new StringBuilder();
                var fullResponseBuilder = new StringBuilder();

                await foreach (var chunk in chatSession.StreamContentAsync(message.Content)) 
                {
                    currentMessageBuilder.Append(chunk.Text);
                    fullResponseBuilder.Append(chunk.Text);
                    yield return currentMessageBuilder.ToString();
                }

                string llmResponse = fullResponseBuilder.ToString().Trim();
                _logger.LogDebug("Gemini raw response (Cycle {Cycle}): {Response}", cycle + 1, llmResponse);

                if (McpToolHelper.TryParseToolCalls(llmResponse, out var toolCalls) && toolCalls.Any())
                {
                    var firstToolCall = toolCalls[0];
                    _logger.LogInformation("Gemini requested tool: {ToolName} with args: {Args}", 
                        firstToolCall.toolName, 
                        JsonConvert.SerializeObject(firstToolCall.arguments));

                    string toolResult;
                    bool isError = false;
                    try
                    {
                        var toolContext = new ToolContext { ChatId = ChatId };
                        var result = await McpToolHelper.ExecuteRegisteredToolAsync(
                            firstToolCall.toolName, 
                            firstToolCall.arguments, 
                            toolContext);
                        toolResult = McpToolHelper.ConvertToolResultToString(result);
                    }
                    catch (Exception ex)
                    {
                        isError = true;
                        _logger.LogError(ex, "Error executing tool {ToolName}", firstToolCall.toolName);
                        toolResult = $"Error executing tool {firstToolCall.toolName}: {ex.Message}";
                    }

                    string feedback = isError 
                        ? $"[Tool '{firstToolCall.toolName}' execution failed: {toolResult}]" 
                        : $"[Tool '{firstToolCall.toolName}' result: {toolResult}]";
                    
                    message.Content = feedback;
                    continue;
                }

                yield break;
            }

            yield return "Maximum tool call cycles reached. Please try again.";
        }
    }
}

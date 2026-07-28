using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Common;
using TelegramSearchBot.Controller.AI.ASR;
using TelegramSearchBot.Controller.AI.OCR;
using TelegramSearchBot.Controller.AI.QR;
using TelegramSearchBot.Controller.Storage;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Manage;
using TelegramMessage = Telegram.Bot.Types.Message;

namespace TelegramSearchBot.Controller.AI.LLM {
    public sealed class AgentChatModeController : IOnUpdate {
        private readonly IGroupLlmSettingsService _groupLlmSettingsService;
        private readonly IBotIdentityProvider _botIdentityProvider;
        private readonly ITelegramBotClient _botClient;
        private readonly AdminService _adminService;
        private readonly ISendMessageService _sendMessageService;
        private readonly AgentChatExecutionService _executionService;
        private readonly AgentChatBatchQueueService _batchQueueService;
        private readonly IConnectionMultiplexer _redis;
        private readonly LlmVisibilityService _llmVisibilityService;
        private readonly ILogger<AgentChatModeController> _logger;

        public AgentChatModeController(
            IGroupLlmSettingsService groupLlmSettingsService,
            IBotIdentityProvider botIdentityProvider,
            ITelegramBotClient botClient,
            AdminService adminService,
            ISendMessageService sendMessageService,
            AgentChatExecutionService executionService,
            AgentChatBatchQueueService batchQueueService,
            IConnectionMultiplexer redis,
            LlmVisibilityService llmVisibilityService,
            ILogger<AgentChatModeController> logger) {
            _groupLlmSettingsService = groupLlmSettingsService;
            _botIdentityProvider = botIdentityProvider;
            _botClient = botClient;
            _adminService = adminService;
            _sendMessageService = sendMessageService;
            _executionService = executionService;
            _batchQueueService = batchQueueService;
            _redis = redis;
            _llmVisibilityService = llmVisibilityService;
            _logger = logger;
        }

        public List<Type> Dependencies => new() {
            typeof(MessageController),
            typeof(AutoOCRController),
            typeof(AutoQRController),
            typeof(AutoASRController)
        };

        public async Task ExecuteAsync(PipelineContext p) {
            var telegramMessage = p.Update.Message;
            if (p.BotMessageType != BotMessageType.Message || telegramMessage == null || telegramMessage.Chat.Id > 0) {
                return;
            }

            var messageText = telegramMessage.Text ?? telegramMessage.Caption ?? string.Empty;
            if (TryParseAgentChatCommand(messageText, out var command)) {
                await HandleAgentChatCommandAsync(telegramMessage, command);
                return;
            }

            if (!Env.EnableOpenAI) {
                return;
            }

            var settings = await _groupLlmSettingsService.GetAgentChatSettingsAsync(telegramMessage.Chat.Id);
            if (!settings.IsEnabled) {
                return;
            }

            var senderUserId = telegramMessage.From?.Id ?? 0;
            if (senderUserId != 0 && await _llmVisibilityService.IsUserInvisibleAsync(telegramMessage.Chat.Id, senderUserId)) {
                _logger.LogInformation(
                    "User {UserId} in chat {ChatId} is LLM invisible; skipping agent chat for message {MessageId}.",
                    senderUserId,
                    telegramMessage.Chat.Id,
                    telegramMessage.MessageId);
                return;
            }

            var botIdentity = await EnsureBotIdentityAsync();
            if (ShouldIgnoreMessage(telegramMessage, messageText, botIdentity)) {
                return;
            }

            if (await HasPendingConfigurationStateAsync(telegramMessage.Chat.Id, telegramMessage.From?.Id ?? 0)) {
                return;
            }

            if (!Env.EnableLLMAgentProcess) {
                await SendRateLimitedWarningAsync(
                    telegramMessage.Chat.Id,
                    telegramMessage.MessageId,
                    "agent-disabled",
                    "Agent 聊天模式需要先启用 EnableLLMAgentProcess=true。");
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.ModelName)) {
                await SendRateLimitedWarningAsync(
                    telegramMessage.Chat.Id,
                    telegramMessage.MessageId,
                    "model-missing",
                    "Agent 聊天模式已开启，但当前群还没有设置 LLM 模型。请先发送 `选择模型` 或 `设置模型 <模型名>`。");
                return;
            }

            var inputMessage = BuildInputMessage(telegramMessage, p.ProcessingResults);
            if (string.IsNullOrWhiteSpace(inputMessage)) {
                return;
            }

            var messageInput = AgentChatMessageInput.FromTelegramMessage(telegramMessage, inputMessage);
            if (settings.Mode == GroupAgentChatMode.Sequential) {
                await _executionService.ExecuteAsync(new AgentChatExecutionRequest {
                    ReplyTarget = messageInput,
                    InputMessage = BuildSingleInput(messageInput),
                    BotName = botIdentity.UserName,
                    BotUserId = botIdentity.UserId,
                    ModelName = settings.ModelName,
                    Mode = GroupAgentChatMode.Sequential
                }, CancellationToken.None);
                return;
            }

            await _batchQueueService.BufferAsync(
                messageInput,
                botIdentity.UserName,
                botIdentity.UserId,
                settings.BatchWindowSeconds,
                CancellationToken.None);
        }

        private async Task HandleAgentChatCommandAsync(TelegramMessage telegramMessage, AgentChatCommand command) {
            var userId = telegramMessage.From?.Id ?? 0;
            var canManage = _adminService.IsGlobalAdmin(userId) || await _adminService.IsNormalAdmin(userId);
            if (!canManage) {
                await _sendMessageService.SendMessage("只有管理员可以调整 Agent 聊天模式。", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return;
            }

            GroupAgentChatSettings settings;
            switch (command) {
                case AgentChatCommand.EnableGuided:
                    settings = await _groupLlmSettingsService.SetAgentChatModeAsync(
                        telegramMessage.Chat.Id,
                        true,
                        GroupAgentChatMode.GuidedBatch);
                    await _sendMessageService.SendMessage(BuildStatusMessage(settings, "Agent 聊天已开启。"), telegramMessage.Chat.Id, telegramMessage.MessageId);
                    return;
                case AgentChatCommand.EnableSequential:
                    settings = await _groupLlmSettingsService.SetAgentChatModeAsync(
                        telegramMessage.Chat.Id,
                        true,
                        GroupAgentChatMode.Sequential);
                    await _sendMessageService.SendMessage(BuildStatusMessage(settings, "Agent 队列聊天已开启。"), telegramMessage.Chat.Id, telegramMessage.MessageId);
                    return;
                case AgentChatCommand.Disable:
                    var current = await _groupLlmSettingsService.GetAgentChatSettingsAsync(telegramMessage.Chat.Id);
                    settings = await _groupLlmSettingsService.SetAgentChatModeAsync(
                        telegramMessage.Chat.Id,
                        false,
                        current.Mode);
                    await _sendMessageService.SendMessage(BuildStatusMessage(settings, "Agent 聊天已关闭。"), telegramMessage.Chat.Id, telegramMessage.MessageId);
                    return;
                case AgentChatCommand.Status:
                    settings = await _groupLlmSettingsService.GetAgentChatSettingsAsync(telegramMessage.Chat.Id);
                    await _sendMessageService.SendMessage(BuildStatusMessage(settings, "Agent 聊天状态："), telegramMessage.Chat.Id, telegramMessage.MessageId);
                    return;
            }
        }

        private async Task<BotIdentity> EnsureBotIdentityAsync() {
            var botIdentity = await _botIdentityProvider.GetIdentityAsync();
            if (!string.IsNullOrEmpty(botIdentity.UserName)) {
                return botIdentity;
            }

            var me = await _botClient.GetMe();
            _botIdentityProvider.SetIdentity(me.Id, me.Username);
            return new BotIdentity(me.Id, me.Username ?? string.Empty);
        }

        private bool ShouldIgnoreMessage(TelegramMessage telegramMessage, string messageText, BotIdentity botIdentity) {
            if (telegramMessage.From?.IsBot == true || telegramMessage.From?.Id == botIdentity.UserId) {
                return true;
            }

            if (IsBotCommand(telegramMessage) || IsKnownManagementCommand(messageText)) {
                return true;
            }

            var isMentionToBot = !string.IsNullOrEmpty(botIdentity.UserName) && messageText.Contains($"@{botIdentity.UserName}", StringComparison.OrdinalIgnoreCase);
            var isReplyToBot = telegramMessage.ReplyToMessage?.From?.Id == botIdentity.UserId;
            return isMentionToBot || isReplyToBot;
        }

        private static bool IsBotCommand(TelegramMessage telegramMessage) {
            return telegramMessage.Entities?.Any(entity =>
                entity.Type == MessageEntityType.BotCommand &&
                entity.Offset == 0) == true;
        }

        private static bool IsKnownManagementCommand(string messageText) {
            var text = messageText.Trim();
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }

            return text.Equals("选择模型", StringComparison.OrdinalIgnoreCase)
                   || text.StartsWith("设置模型 ", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("选择生图模型", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("生图模型列表", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("可用生图模型", StringComparison.OrdinalIgnoreCase)
                   || text.StartsWith("设置生图模型 ", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("清除生图模型", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("重置生图模型", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("生图模型", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("查看生图模型", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("选择音乐模型", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("音乐模型列表", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("可用音乐模型", StringComparison.OrdinalIgnoreCase)
                   || text.StartsWith("设置音乐模型 ", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("清除音乐模型", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("重置音乐模型", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("音乐模型", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("查看音乐模型", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildInputMessage(TelegramMessage telegramMessage, IReadOnlyCollection<string> processingResults) {
            var raw = telegramMessage.Text ?? telegramMessage.Caption ?? string.Empty;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(raw)) {
                parts.Add(raw.Trim());
            }

            foreach (var result in processingResults.Where(x => !string.IsNullOrWhiteSpace(x))) {
                var trimmed = result.Trim();
                if (string.Equals(trimmed, raw.Trim(), StringComparison.Ordinal)) {
                    continue;
                }

                if (parts.Any(x => string.Equals(x, trimmed, StringComparison.Ordinal))) {
                    continue;
                }

                parts.Add(trimmed);
            }

            return string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        private static string BuildSingleInput(AgentChatMessageInput message) {
            return $"""
                以下是群内的一条消息，请作为 Agent 输入处理。

                --- 消息 | MessageId={message.MessageId} | User={FormatUser(message)} | Time={message.DateTime:O} ---
                {message.Content.Trim()}
                """;
        }

        private static string BuildStatusMessage(GroupAgentChatSettings settings, string title) {
            var modeName = settings.Mode == GroupAgentChatMode.Sequential ? "队列模式（逐条串行）" : "引导模式（短窗口合并）";
            var enabled = settings.IsEnabled ? "开启" : "关闭";
            var modelName = string.IsNullOrWhiteSpace(settings.ModelName) ? "未设置" : settings.ModelName;
            return $"{title}\n状态：{enabled}\n模式：{modeName}\n模型：{modelName}\n合并窗口：{settings.BatchWindowSeconds} 秒";
        }

        private async Task SendRateLimitedWarningAsync(long chatId, int replyToMessageId, string warningType, string message) {
            var db = _redis.GetDatabase();
            if (await db.StringSetAsync(LlmAgentRedisKeys.AgentChatConfigWarning(chatId, warningType), "1", TimeSpan.FromMinutes(1), When.NotExists)) {
                await _sendMessageService.SendMessage(message, chatId, replyToMessageId);
            }
        }

        private async Task<bool> HasPendingConfigurationStateAsync(long chatId, long userId) {
            var db = _redis.GetDatabase();
            var pendingKeys = new[] {
                LlmAgentRedisKeys.ModelSelectState(chatId),
                LlmAgentRedisKeys.ImageGenerationModelSelection(chatId, userId),
                LlmAgentRedisKeys.MusicGenerationModelSelection(chatId, userId)
            };

            foreach (var key in pendingKeys) {
                if (( await db.StringGetAsync(key) ).HasValue) {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseAgentChatCommand(string messageText, out AgentChatCommand command) {
            var normalized = messageText.Trim().Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            command = AgentChatCommand.None;
            if (string.IsNullOrWhiteSpace(normalized)) {
                return false;
            }

            if (normalized.Equals("开启Agent聊天", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("开启Agent引导聊天", StringComparison.OrdinalIgnoreCase)) {
                command = AgentChatCommand.EnableGuided;
                return true;
            }

            if (normalized.Equals("开启Agent队列聊天", StringComparison.OrdinalIgnoreCase)) {
                command = AgentChatCommand.EnableSequential;
                return true;
            }

            if (normalized.Equals("关闭Agent聊天", StringComparison.OrdinalIgnoreCase)) {
                command = AgentChatCommand.Disable;
                return true;
            }

            if (normalized.Equals("Agent聊天状态", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("查看Agent聊天", StringComparison.OrdinalIgnoreCase)) {
                command = AgentChatCommand.Status;
                return true;
            }

            return false;
        }

        private static string FormatUser(AgentChatMessageInput message) {
            var displayName = $"{message.FirstName} {message.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(displayName)) {
                displayName = string.IsNullOrWhiteSpace(message.Username) ? message.UserId.ToString() : $"@{message.Username}";
            }

            return $"{displayName} ({message.UserId})";
        }

        private enum AgentChatCommand {
            None = 0,
            EnableGuided = 1,
            EnableSequential = 2,
            Disable = 3,
            Status = 4
        }
    }
}

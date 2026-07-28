using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Transient)]
    public sealed class AgentChatExecutionService : IService {
        private readonly LLMTaskQueueService _taskQueueService;
        private readonly ISendMessageService _sendMessageService;
        private readonly MessageService _messageService;
        private readonly ITelegramBotClient _botClient;
        private readonly ILlmContinuationService _continuationService;
        private readonly ILogger<AgentChatExecutionService> _logger;

        public AgentChatExecutionService(
            LLMTaskQueueService taskQueueService,
            ISendMessageService sendMessageService,
            MessageService messageService,
            ITelegramBotClient botClient,
            ILlmContinuationService continuationService,
            ILogger<AgentChatExecutionService> logger) {
            _taskQueueService = taskQueueService;
            _sendMessageService = sendMessageService;
            _messageService = messageService;
            _botClient = botClient;
            _continuationService = continuationService;
            _logger = logger;
        }

        public string ServiceName => nameof(AgentChatExecutionService);

        public async Task ExecuteAsync(AgentChatExecutionRequest request, CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(request.InputMessage)) {
                return;
            }

            AgentTaskStreamHandle agentTaskHandle;
            try {
                agentTaskHandle = await _taskQueueService.EnqueueMessageTaskAsync(
                    request.ReplyTarget.ChatId,
                    request.ReplyTarget.UserId,
                    request.ReplyTarget.MessageId,
                    request.ReplyTarget.DateTime,
                    request.InputMessage,
                    request.BotName,
                    request.BotUserId,
                    cancellationToken);
            } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) {
                _logger.LogWarning(ex, "Agent chat execution could not be queued. ChatId={ChatId}, MessageId={MessageId}",
                    request.ReplyTarget.ChatId,
                    request.ReplyTarget.MessageId);
                await _sendMessageService.SendMessage($"Agent 聊天无法执行：{ex.Message}", request.ReplyTarget.ChatId, request.ReplyTarget.MessageId);
                return;
            }

            var initialContentPlaceholder = $"{( string.IsNullOrWhiteSpace(request.ModelName) ? "Agent" : request.ModelName )}初始化中。。。";
            List<TelegramSearchBot.Model.Data.Message> sentMessagesForDb = await _sendMessageService.SendDraftStream(
                agentTaskHandle.ReadSnapshotsAsync(cancellationToken),
                request.ReplyTarget.ChatId,
                request.ReplyTarget.MessageId,
                initialContentPlaceholder,
                cancellationToken);

            await SaveBotMessagesAsync(request, sentMessagesForDb);
            await HandleTerminalChunkAsync(request, await agentTaskHandle.Completion);
        }

        private async Task SaveBotMessagesAsync(AgentChatExecutionRequest request, List<TelegramSearchBot.Model.Data.Message> sentMessagesForDb) {
            if (sentMessagesForDb.Count == 0) {
                return;
            }

            User botUser = await _botClient.GetMe();
            var chat = request.ReplyTarget.ToTelegramChat();
            foreach (var dbMessage in sentMessagesForDb) {
                await _messageService.ExecuteAsync(new MessageOption {
                    Chat = chat,
                    ChatId = dbMessage.GroupId,
                    Content = dbMessage.Content,
                    DateTime = dbMessage.DateTime,
                    MessageId = dbMessage.MessageId,
                    User = botUser,
                    ReplyTo = dbMessage.ReplyToMessageId,
                    UserId = dbMessage.FromUserId,
                });
            }
        }

        private async Task HandleTerminalChunkAsync(AgentChatExecutionRequest request, AgentStreamChunk terminalChunk) {
            if (terminalChunk.Type == AgentChunkType.Error) {
                _logger.LogError(
                    "Agent chat execution failed. ChatId={ChatId}, MessageId={MessageId}, ErrorMessage={ErrorMessage}",
                    request.ReplyTarget.ChatId,
                    request.ReplyTarget.MessageId,
                    terminalChunk.ErrorMessage);
                await _sendMessageService.SendMessage($"AI Agent 执行失败：{terminalChunk.ErrorMessage}", request.ReplyTarget.ChatId, request.ReplyTarget.MessageId);
                return;
            }

            if (terminalChunk.Type != AgentChunkType.IterationLimitReached || terminalChunk.ContinuationSnapshot == null) {
                return;
            }

            var snapshotId = await _continuationService.SaveSnapshotAsync(terminalChunk.ContinuationSnapshot);
            var keyboard = new InlineKeyboardMarkup(new[] {
                new[] {
                    InlineKeyboardButton.WithCallbackData("✅ 继续迭代", $"llm_continue:{snapshotId}"),
                    InlineKeyboardButton.WithCallbackData("❌ 停止", $"llm_stop:{snapshotId}"),
                }
            });

            await _botClient.SendMessage(
                request.ReplyTarget.ChatId,
                $"⚠️ AI 已达到最大迭代次数限制（{Env.MaxToolCycles} 次），是否继续迭代？",
                replyMarkup: keyboard,
                replyParameters: new ReplyParameters { MessageId = request.ReplyTarget.MessageId }
            );
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Controller.AI.LLM {
    /// <summary>
    /// 处理用户点击 "继续迭代" / "停止" InlineButton 的回调。
    /// 当 AI Agent 达到 MaxToolCycles 限制时，GeneralLLMController 会发送
    /// 一条带 InlineKeyboard 的确认消息，callback data 中携带 snapshotId。
    /// 本控制器处理该回调：
    /// - 继续：从 Redis 加载快照，恢复完整 LLM 上下文，无缝继续
    /// - 停止：删除快照，移除键盘
    /// </summary>
    public class LLMIterationCallbackController : IOnUpdate {
        private const string ContinuePrefix = "llm_continue:";
        private const string StopPrefix = "llm_stop:";

        private readonly ILogger<LLMIterationCallbackController> _logger;
        private readonly ITelegramBotClient _botClient;
        private readonly IGeneralLLMService _generalLLMService;
        private readonly ISendMessageService _sendMessageService;
        private readonly MessageService _messageService;
        private readonly ILlmContinuationService _continuationService;

        public List<Type> Dependencies => new List<Type>();

        public LLMIterationCallbackController(
            ILogger<LLMIterationCallbackController> logger,
            ITelegramBotClient botClient,
            IGeneralLLMService generalLLMService,
            ISendMessageService sendMessageService,
            MessageService messageService,
            ILlmContinuationService continuationService) {
            _logger = logger;
            _botClient = botClient;
            _generalLLMService = generalLLMService;
            _sendMessageService = sendMessageService;
            _messageService = messageService;
            _continuationService = continuationService;
        }

        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            if (e.CallbackQuery == null) return;

            var data = e.CallbackQuery.Data;
            if (string.IsNullOrEmpty(data)) return;

            if (data.StartsWith(ContinuePrefix)) {
                var snapshotId = data.Substring(ContinuePrefix.Length);
                await HandleContinue(e, snapshotId);
            } else if (data.StartsWith(StopPrefix)) {
                var snapshotId = data.Substring(StopPrefix.Length);
                await HandleStop(e, snapshotId);
            }
            // Not our callback, ignore
        }

        private async Task HandleStop(Telegram.Bot.Types.Update e, string snapshotId) {
            _logger.LogInformation("User {UserId} chose to stop LLM iteration. SnapshotId: {SnapshotId}",
                e.CallbackQuery.From.Id, snapshotId);

            await _botClient.AnswerCallbackQuery(e.CallbackQuery.Id, "已停止迭代");

            // Remove the inline keyboard
            try {
                if (e.CallbackQuery.Message != null) {
                    await _botClient.EditMessageReplyMarkup(
                        e.CallbackQuery.Message.Chat.Id,
                        e.CallbackQuery.Message.MessageId,
                        replyMarkup: null
                    );
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to remove inline keyboard after stop.");
            }

            // Clean up the snapshot
            await _continuationService.DeleteSnapshotAsync(snapshotId);
        }

        private async Task HandleContinue(Telegram.Bot.Types.Update e, string snapshotId) {
            _logger.LogInformation("User {UserId} chose to continue LLM iteration. SnapshotId: {SnapshotId}",
                e.CallbackQuery.From.Id, snapshotId);

            // Try to acquire lock to prevent duplicate execution
            if (!await _continuationService.TryAcquireLockAsync(snapshotId)) {
                await _botClient.AnswerCallbackQuery(e.CallbackQuery.Id, "⏳ 已在处理中，请勿重复点击");
                return;
            }

            try {
                // Load the snapshot
                var snapshot = await _continuationService.GetSnapshotAsync(snapshotId);
                if (snapshot == null) {
                    await _botClient.AnswerCallbackQuery(e.CallbackQuery.Id, "⚠️ 上下文已过期或不存在，无法继续");
                    // Remove keyboard
                    try {
                        if (e.CallbackQuery.Message != null) {
                            await _botClient.EditMessageReplyMarkup(
                                e.CallbackQuery.Message.Chat.Id,
                                e.CallbackQuery.Message.MessageId,
                                replyMarkup: null
                            );
                        }
                    } catch (Exception ex) {
                        _logger.LogWarning(ex, "Failed to remove inline keyboard after snapshot expiry.");
                    }
                    return;
                }

                await _botClient.AnswerCallbackQuery(e.CallbackQuery.Id, "继续迭代中...");

                // Remove the inline keyboard
                try {
                    if (e.CallbackQuery.Message != null) {
                        await _botClient.EditMessageReplyMarkup(
                            e.CallbackQuery.Message.Chat.Id,
                            e.CallbackQuery.Message.MessageId,
                            replyMarkup: null
                        );
                    }
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Failed to remove inline keyboard after continue.");
                }

                _logger.LogInformation(
                    "Resuming from snapshot {SnapshotId}: ChatId={ChatId}, Provider={Provider}, HistoryEntries={HistoryCount}, CyclesSoFar={CyclesSoFar}",
                    snapshotId, snapshot.ChatId, snapshot.Provider,
                    snapshot.ProviderHistory?.Count ?? 0, snapshot.CyclesSoFar);

                // Resume with full context — yields only NEW content (not re-sending old history)
                var executionContext = new LlmExecutionContext();
                IAsyncEnumerable<string> resumeStream = _generalLLMService.ResumeFromSnapshotAsync(
                    snapshot, executionContext, CancellationToken.None);

                var initialContent = $"{snapshot.ModelName} 继续迭代中...";
                // Use SendDraftStream for continuation — only new content is streamed
                List<Model.Data.Message> sentMessagesForDb = await _sendMessageService.SendDraftStream(
                    resumeStream,
                    snapshot.ChatId,
                    ( int ) snapshot.OriginalMessageId,
                    initialContent,
                    CancellationToken.None
                );

                // Save sent messages to DB
                User botUser = null;
                var chat = e.CallbackQuery.Message?.Chat;
                foreach (var dbMessage in sentMessagesForDb) {
                    if (botUser == null) {
                        botUser = await _botClient.GetMe();
                    }
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

                // Delete the used snapshot
                await _continuationService.DeleteSnapshotAsync(snapshotId);

                // If iteration limit reached again, save new snapshot and show prompt
                if (executionContext.IterationLimitReached && executionContext.SnapshotData != null) {
                    _logger.LogInformation("Iteration limit reached again after continuation for ChatId {ChatId}.", snapshot.ChatId);

                    var newSnapshotId = await _continuationService.SaveSnapshotAsync(executionContext.SnapshotData);

                    var keyboard = new InlineKeyboardMarkup(new[] {
                        new[] {
                            InlineKeyboardButton.WithCallbackData("✅ 继续迭代", $"llm_continue:{newSnapshotId}"),
                            InlineKeyboardButton.WithCallbackData("❌ 停止", $"llm_stop:{newSnapshotId}"),
                        }
                    });

                    await _botClient.SendMessage(
                        snapshot.ChatId,
                        $"⚠️ AI 再次达到最大迭代次数限制（{Env.MaxToolCycles} 次），已完成 {executionContext.SnapshotData.CyclesSoFar} 次循环，是否继续迭代？",
                        replyMarkup: keyboard,
                        replyParameters: new ReplyParameters { MessageId = ( int ) snapshot.OriginalMessageId }
                    );
                }
            } finally {
                // Always release the lock
                await _continuationService.ReleaseLockAsync(snapshotId);
            }
        }
    }
}

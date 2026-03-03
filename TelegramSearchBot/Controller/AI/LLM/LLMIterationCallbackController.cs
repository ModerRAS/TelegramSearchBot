using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Common;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Controller.AI.LLM {
    /// <summary>
    /// 处理用户点击 "继续迭代" / "停止" InlineButton 的回调。
    /// 当 AI Agent 达到 MaxToolCycles 限制时，GeneralLLMController 会发送
    /// 一条带 InlineKeyboard 的确认消息。本控制器处理该回调：
    /// - 继续：重新调用 LLM 继续对话（迭代次数重新计数）
    /// - 停止：保留当前内容，不再继续
    /// </summary>
    public class LLMIterationCallbackController : IOnUpdate {
        private const string ContinuePrefix = "llm_continue:";
        private const string StopPrefix = "llm_stop:";

        private readonly ILogger<LLMIterationCallbackController> _logger;
        private readonly ITelegramBotClient _botClient;
        private readonly IGeneralLLMService _generalLLMService;
        private readonly ISendMessageService _sendMessageService;
        private readonly MessageService _messageService;
        private readonly OpenAIService _openAIService;
        private readonly DataDbContext _dbContext;

        public List<Type> Dependencies => new List<Type>();

        public LLMIterationCallbackController(
            ILogger<LLMIterationCallbackController> logger,
            ITelegramBotClient botClient,
            IGeneralLLMService generalLLMService,
            ISendMessageService sendMessageService,
            MessageService messageService,
            OpenAIService openAIService,
            DataDbContext dbContext) {
            _logger = logger;
            _botClient = botClient;
            _generalLLMService = generalLLMService;
            _sendMessageService = sendMessageService;
            _messageService = messageService;
            _openAIService = openAIService;
            _dbContext = dbContext;
        }

        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            if (e.CallbackQuery == null) return;

            var data = e.CallbackQuery.Data;
            if (string.IsNullOrEmpty(data)) return;

            if (data.StartsWith(ContinuePrefix)) {
                await HandleContinue(e, data);
            } else if (data.StartsWith(StopPrefix)) {
                await HandleStop(e);
            }
            // Not our callback, ignore
        }

        private async Task HandleStop(Telegram.Bot.Types.Update e) {
            _logger.LogInformation("User {UserId} chose to stop LLM iteration.", e.CallbackQuery.From.Id);

            await _botClient.AnswerCallbackQuery(e.CallbackQuery.Id, "已停止迭代");

            // Remove the inline keyboard from the prompt message
            try {
                if (e.CallbackQuery.Message != null) {
                    await _botClient.EditMessageReplyMarkup(
                        e.CallbackQuery.Message.Chat.Id,
                        e.CallbackQuery.Message.MessageId,
                        replyMarkup: null // Remove inline keyboard
                    );
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to remove inline keyboard after stop.");
            }
        }

        private async Task HandleContinue(Telegram.Bot.Types.Update e, string data) {
            // Parse callback data: "llm_continue:{chatId}:{originalMessageId}"
            var parts = data.Substring(ContinuePrefix.Length).Split(':');
            if (parts.Length < 2 ||
                !long.TryParse(parts[0], out var chatId) ||
                !int.TryParse(parts[1], out var originalMessageId)) {
                _logger.LogWarning("Invalid llm_continue callback data: {Data}", data);
                await _botClient.AnswerCallbackQuery(e.CallbackQuery.Id, "回调数据无效");
                return;
            }

            _logger.LogInformation("User {UserId} chose to continue LLM iteration for ChatId {ChatId}, OriginalMessageId {MsgId}.",
                e.CallbackQuery.From.Id, chatId, originalMessageId);

            await _botClient.AnswerCallbackQuery(e.CallbackQuery.Id, "继续迭代中...");

            // Remove the inline keyboard from the prompt message
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

            // Look up the original user message from the database
            var originalMessage = await _dbContext.Messages
                .Where(m => m.GroupId == chatId && m.MessageId == originalMessageId)
                .FirstOrDefaultAsync();

            if (originalMessage == null) {
                _logger.LogWarning("Could not find original message {MsgId} in chat {ChatId} for LLM continuation.", originalMessageId, chatId);
                await _sendMessageService.SendMessage("⚠️ 找不到原始消息，无法继续迭代。", chatId, originalMessageId);
                return;
            }

            // Build a continuation message - the LLM will see the full history (including 
            // its previous responses) from the database and continue from where it left off.
            var continuationMessage = new Model.Data.Message {
                Content = originalMessage.Content,
                DateTime = DateTime.UtcNow,
                FromUserId = e.CallbackQuery.From.Id,
                GroupId = chatId,
                MessageId = originalMessageId,
                ReplyToMessageId = originalMessage.ReplyToMessageId,
                Id = -1,
            };

            var modelName = await _openAIService.GetModel(chatId);
            var initialContent = $"{modelName} 继续迭代中...";

            // Re-invoke the LLM with the same conversation context
            IAsyncEnumerable<string> fullMessageStream = _generalLLMService.ExecAsync(
                continuationMessage, chatId, CancellationToken.None);

            // Use the same iteration limit detection wrapper
            var iterationLimitDetector = new IterationLimitAwareStream();
            IAsyncEnumerable<string> wrappedStream = iterationLimitDetector.WrapAsync(fullMessageStream, CancellationToken.None);

            List<Model.Data.Message> sentMessagesForDb = await _sendMessageService.SendFullMessageStream(
                wrappedStream,
                chatId,
                originalMessageId,
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

            // If iteration limit reached again, show the prompt again
            if (iterationLimitDetector.IterationLimitReached) {
                _logger.LogInformation("Iteration limit reached again after continuation for ChatId {ChatId}.", chatId);

                var keyboard = new InlineKeyboardMarkup(new[] {
                    new[] {
                        InlineKeyboardButton.WithCallbackData("✅ 继续迭代", $"llm_continue:{chatId}:{originalMessageId}"),
                        InlineKeyboardButton.WithCallbackData("❌ 停止", $"llm_stop:{chatId}:{originalMessageId}"),
                    }
                });

                await _botClient.SendMessage(
                    chatId,
                    $"⚠️ AI 再次达到最大迭代次数限制（{Env.MaxToolCycles} 次），是否继续迭代？",
                    replyMarkup: keyboard,
                    replyParameters: new ReplyParameters { MessageId = originalMessageId }
                );
            }
        }
    }
}

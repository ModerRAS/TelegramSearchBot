using System;
using System.Collections.Generic;
using System.Linq; // Added for LINQ methods
using System.Threading; // For CancellationToken
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums; // Added for MessageEntityType
using Telegram.Bot.Types.ReplyMarkups; // For InlineKeyboardMarkup
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Controller.AI.LLM {
    public class GeneralLLMController : IOnUpdate {
        private readonly ILogger logger;
        private readonly SendMessage Send;
        private readonly IBotIdentityProvider _botIdentityProvider;
        private readonly IGroupLlmSettingsService _groupLlmSettingsService;
        public List<Type> Dependencies => new List<Type>();
        public ITelegramBotClient botClient { get; set; }
        public MessageService messageService { get; set; }
        public AdminService adminService { get; set; }
        public ISendMessageService SendMessageService { get; set; }
        public IGeneralLLMService GeneralLLMService { get; set; }
        public ILlmContinuationService ContinuationService { get; set; }
        public LLMTaskQueueService LlmTaskQueueService { get; set; }
        public GeneralLLMController(
            MessageService messageService,
            ITelegramBotClient botClient,
            SendMessage Send,
            ILogger<GeneralLLMController> logger,
            AdminService adminService,
            ISendMessageService SendMessageService,
            IGeneralLLMService generalLLMService,
            ILlmContinuationService continuationService,
            LLMTaskQueueService llmTaskQueueService,
            IBotIdentityProvider botIdentityProvider,
            IGroupLlmSettingsService groupLlmSettingsService
            ) {
            this.logger = logger;
            this.botClient = botClient;
            this.Send = Send;
            this.messageService = messageService;
            this.adminService = adminService;
            this.SendMessageService = SendMessageService;
            GeneralLLMService = generalLLMService;
            ContinuationService = continuationService;
            LlmTaskQueueService = llmTaskQueueService;
            _botIdentityProvider = botIdentityProvider;
            _groupLlmSettingsService = groupLlmSettingsService;

        }
        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            var telegramMessage = e.Message;
            if (telegramMessage == null) {
                return;
            }

            if (!Env.EnableOpenAI) {
                return;
            }
            var botIdentity = await _botIdentityProvider.GetIdentityAsync();
            if (string.IsNullOrEmpty(botIdentity.UserName)) {
                var me = await botClient.GetMe();
                _botIdentityProvider.SetIdentity(me.Id, me.Username);
                botIdentity = new BotIdentity(me.Id, me.Username ?? string.Empty);
            }

            var Message = string.IsNullOrEmpty(telegramMessage.Text) ? telegramMessage.Caption : telegramMessage.Text;
            if (string.IsNullOrEmpty(Message)) {
                return;
            }
            var fromUserId = telegramMessage.From?.Id ?? 0;

            // Check if the message is a bot command specifically targeting this bot
            // Bot identity should be initialized by the block above.
            if (telegramMessage.Entities != null && !string.IsNullOrEmpty(botIdentity.UserName) &&
                telegramMessage.Entities.Any(entity => entity.Type == MessageEntityType.BotCommand)) {
                var botCommandEntity = telegramMessage.Entities.First(entity => entity.Type == MessageEntityType.BotCommand);
                // Ensure the command is at the beginning of the message
                if (botCommandEntity.Offset == 0) {
                    string commandText = Message.Substring(botCommandEntity.Offset, botCommandEntity.Length);
                    // Check if the command text itself contains @BotName (e.g., /cmd@MyBot)
                    if (commandText.Contains($"@{botIdentity.UserName}")) {
                        logger.LogInformation($"Ignoring command '{commandText}' in GeneralLLMController as it's a direct command to the bot and should be handled by a dedicated command handler. MessageId: {telegramMessage.MessageId}");
                        return; // Let other command handlers process it
                    }
                }
            }

            if (Message.StartsWith("设置模型 ") && fromUserId != 0 && await adminService.IsNormalAdmin(fromUserId)) {
                var requestedModelName = Message.Substring(5).Trim();
                if (string.IsNullOrWhiteSpace(requestedModelName)) {
                    await SendMessageService.SendMessage("模型名称不能为空", telegramMessage.Chat.Id, telegramMessage.MessageId);
                    return;
                }

                var (previous, current) = await _groupLlmSettingsService.SetModelAsync(telegramMessage.Chat.Id, requestedModelName);
                logger.LogInformation($"群{telegramMessage.Chat.Id}模型设置成功，原模型：{previous}，现模型：{current}。消息来源：{telegramMessage.MessageId}");
                await SendMessageService.SendMessage($"模型设置成功，原模型：{previous}，现模型：{current}", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return;
            }

            // Trigger LLM if:
            // 1. Message contains an explicit mention @BotName (and bot identity is set)
            // 2. Message is a reply to the bot (Env.BotId must be set)
            bool isMentionToBot = !string.IsNullOrEmpty(botIdentity.UserName) && Message.Contains($"@{botIdentity.UserName}");
            bool isReplyToBot = telegramMessage.ReplyToMessage != null && telegramMessage.ReplyToMessage.From != null && telegramMessage.ReplyToMessage.From.Id == botIdentity.UserId;

            if (isMentionToBot || isReplyToBot) {
                var modelName = await _groupLlmSettingsService.GetModelAsync(telegramMessage.Chat.Id);
                if (string.IsNullOrWhiteSpace(modelName)) {
                    logger.LogWarning("请指定模型名称");
                    return;
                }

                var initialContentPlaceholder = $"{modelName}初始化中。。。";

                // Prepare the input message for GeneralLLMService
                var inputLlMessage = new Model.Data.Message() {
                    Content = Message,
                    DateTime = telegramMessage.Date,
                    FromUserId = fromUserId,
                    GroupId = telegramMessage.Chat.Id,
                    MessageId = telegramMessage.MessageId,
                    ReplyToMessageId = telegramMessage.ReplyToMessage?.MessageId ?? 0,
                    Id = -1,
                };

                // Use execution context to detect iteration limit (no stream pollution)
                var executionContext = new LlmExecutionContext();
                IAsyncEnumerable<string> fullMessageStream;
                AgentTaskStreamHandle agentTaskHandle = null;
                if (Env.EnableLLMAgentProcess) {
                    agentTaskHandle = await LlmTaskQueueService.EnqueueMessageTaskAsync(
                        telegramMessage,
                        botIdentity.UserName,
                        botIdentity.UserId,
                        CancellationToken.None);
                    fullMessageStream = agentTaskHandle.ReadSnapshotsAsync(CancellationToken.None);
                } else {
                    fullMessageStream = GeneralLLMService.ExecAsync(
                        inputLlMessage, telegramMessage.Chat.Id, executionContext, CancellationToken.None);
                }

                // Use sendMessageDraft API for LLM streaming (better performance, no send+edit)
                List<Model.Data.Message> sentMessagesForDb = await SendMessageService.SendDraftStream(
                    fullMessageStream,
                    telegramMessage.Chat.Id,
                    telegramMessage.MessageId,
                    initialContentPlaceholder,
                    CancellationToken.None
                );

                // Process the list of messages returned for DB logging
                User botUser = null;
                foreach (var dbMessage in sentMessagesForDb) {
                    if (botUser == null) {
                        botUser = await botClient.GetMe();
                    }
                    await messageService.ExecuteAsync(new MessageOption() {
                        Chat = telegramMessage.Chat,
                        ChatId = dbMessage.GroupId,
                        Content = dbMessage.Content,
                        DateTime = dbMessage.DateTime,
                        MessageId = dbMessage.MessageId,
                        User = botUser,
                        ReplyTo = dbMessage.ReplyToMessageId,
                        UserId = dbMessage.FromUserId,
                    });
                }

                if (agentTaskHandle != null) {
                    var terminalChunk = await agentTaskHandle.Completion;
                    if (terminalChunk.Type == AgentChunkType.Error) {
                        logger.LogError(
                            "AI Agent 执行失败，ChatId {ChatId}, MessageId {MessageId}, ErrorMessage: {ErrorMessage}",
                            telegramMessage.Chat.Id,
                            telegramMessage.MessageId,
                            terminalChunk.ErrorMessage);
                        await SendMessageService.SendMessage($"AI Agent 执行失败：{terminalChunk.ErrorMessage}", telegramMessage.Chat.Id, telegramMessage.MessageId);
                    } else if (terminalChunk.Type == AgentChunkType.IterationLimitReached && terminalChunk.ContinuationSnapshot != null) {
                        var snapshotId = await ContinuationService.SaveSnapshotAsync(terminalChunk.ContinuationSnapshot);

                        var keyboard = new InlineKeyboardMarkup(new[] {
                            new[] {
                                InlineKeyboardButton.WithCallbackData("✅ 继续迭代", $"llm_continue:{snapshotId}"),
                                InlineKeyboardButton.WithCallbackData("❌ 停止", $"llm_stop:{snapshotId}"),
                            }
                        });

                        await botClient.SendMessage(
                            telegramMessage.Chat.Id,
                            $"⚠️ AI 已达到最大迭代次数限制（{Env.MaxToolCycles} 次），是否继续迭代？",
                            replyMarkup: keyboard,
                            replyParameters: new ReplyParameters { MessageId = telegramMessage.MessageId }
                        );
                    }

                    return;
                }

                // Check if the iteration limit was reached via execution context
                if (executionContext.IterationLimitReached && executionContext.SnapshotData != null) {
                    logger.LogInformation("Iteration limit reached for ChatId {ChatId}, MessageId {MessageId}. Saving snapshot and prompting user.",
                        telegramMessage.Chat.Id, telegramMessage.MessageId);

                    // Save the snapshot to Redis
                    var snapshotId = await ContinuationService.SaveSnapshotAsync(executionContext.SnapshotData);

                    var keyboard = new InlineKeyboardMarkup(new[] {
                        new[] {
                            InlineKeyboardButton.WithCallbackData("✅ 继续迭代", $"llm_continue:{snapshotId}"),
                            InlineKeyboardButton.WithCallbackData("❌ 停止", $"llm_stop:{snapshotId}"),
                        }
                    });

                    await botClient.SendMessage(
                        telegramMessage.Chat.Id,
                        $"⚠️ AI 已达到最大迭代次数限制（{Env.MaxToolCycles} 次），是否继续迭代？",
                        replyMarkup: keyboard,
                        replyParameters: new ReplyParameters { MessageId = telegramMessage.MessageId }
                    );
                }

                return;
            }
        }
    }
}

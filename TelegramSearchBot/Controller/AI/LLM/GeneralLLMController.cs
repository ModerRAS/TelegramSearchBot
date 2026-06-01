using System;
using System.Collections.Generic;
using System.Linq; // Added for LINQ methods
using System.Text;
using System.Threading; // For CancellationToken
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
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
using TelegramSearchBot.Service.Tools;

namespace TelegramSearchBot.Controller.AI.LLM {
    public class GeneralLLMController : IOnUpdate {
        private readonly ILogger logger;
        private readonly SendMessage Send;
        private readonly IBotIdentityProvider _botIdentityProvider;
        private readonly IGroupLlmSettingsService _groupLlmSettingsService;
        private readonly ImageGenerationToolSettingsService _imageGenerationToolSettingsService;
        private readonly MusicGenerationToolSettingsService _musicGenerationToolSettingsService;
        private readonly IModelCapabilityService _modelCapabilityService;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly LlmVisibilityService _llmVisibilityService;
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
            IGroupLlmSettingsService groupLlmSettingsService,
            ImageGenerationToolSettingsService imageGenerationToolSettingsService,
            MusicGenerationToolSettingsService musicGenerationToolSettingsService,
            IModelCapabilityService modelCapabilityService,
            IConnectionMultiplexer connectionMultiplexer,
            LlmVisibilityService llmVisibilityService
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
            _imageGenerationToolSettingsService = imageGenerationToolSettingsService;
            _musicGenerationToolSettingsService = musicGenerationToolSettingsService;
            _modelCapabilityService = modelCapabilityService;
            _connectionMultiplexer = connectionMultiplexer;
            _llmVisibilityService = llmVisibilityService;

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
                        using (LoggerHolders.PushChatContentLogScope()) {
                            logger.LogInformation($"Ignoring command '{commandText}' in GeneralLLMController as it's a direct command to the bot and should be handled by a dedicated command handler. MessageId: {telegramMessage.MessageId}");
                        }
                        return; // Let other command handlers process it
                    }
                }
            }

            var isNormalAdmin = fromUserId != 0 && await adminService.IsNormalAdmin(fromUserId);

            if (isNormalAdmin && await TryHandlePendingImageGenerationModelSelectionAsync(telegramMessage, Message, fromUserId)) {
                return;
            }

            if (isNormalAdmin && await TryHandlePendingMusicGenerationModelSelectionAsync(telegramMessage, Message, fromUserId)) {
                return;
            }

            if (Message.Equals("选择生图模型", StringComparison.OrdinalIgnoreCase) ||
                Message.Equals("生图模型列表", StringComparison.OrdinalIgnoreCase) ||
                Message.Equals("可用生图模型", StringComparison.OrdinalIgnoreCase)) {
                if (!isNormalAdmin) {
                    return;
                }

                var options = await LoadImageGenerationModelOptionsAsync();
                if (options.Count == 0) {
                    await SendMessageService.SendMessage("当前没有识别到可用生图模型。请先通过 `新建渠道` / `添加模型` 关联 `gpt-image-*`、`dall-e*`、MiniMax `image-01` / `image-01-live`，或刷新渠道能力。", telegramMessage.Chat.Id, telegramMessage.MessageId);
                    return;
                }

                await SaveImageGenerationModelSelectionAsync(telegramMessage.Chat.Id, fromUserId, options);
                await SendMessageService.SendMessage(BuildImageGenerationModelSelectionMessage(options), telegramMessage.Chat.Id, telegramMessage.MessageId);
                return;
            }

            if (Message.Equals("选择音乐模型", StringComparison.OrdinalIgnoreCase) ||
                Message.Equals("音乐模型列表", StringComparison.OrdinalIgnoreCase) ||
                Message.Equals("可用音乐模型", StringComparison.OrdinalIgnoreCase)) {
                if (!isNormalAdmin) {
                    return;
                }

                var options = await LoadMusicGenerationModelOptionsAsync();
                if (options.Count == 0) {
                    await SendMessageService.SendMessage("当前没有识别到可用音乐模型。请先通过 `新建渠道` / `添加模型` 关联 MiniMax `music-2.6`、`music-2.6-free`、`music-cover` 或 `music-cover-free`，或刷新渠道能力。", telegramMessage.Chat.Id, telegramMessage.MessageId);
                    return;
                }

                await SaveMusicGenerationModelSelectionAsync(telegramMessage.Chat.Id, fromUserId, options);
                await SendMessageService.SendMessage(BuildMusicGenerationModelSelectionMessage(options), telegramMessage.Chat.Id, telegramMessage.MessageId);
                return;
            }

            if (Message.StartsWith("设置生图模型 ") && isNormalAdmin) {
                var requestedModelName = Message.Substring(7).Trim();
                if (string.IsNullOrWhiteSpace(requestedModelName)) {
                    await SendMessageService.SendMessage("生图模型名称不能为空", telegramMessage.Chat.Id, telegramMessage.MessageId);
                    return;
                }

                var (previous, current) = await _imageGenerationToolSettingsService.SetGroupModelNameAsync(telegramMessage.Chat.Id, requestedModelName);
                logger.LogInformation($"群{telegramMessage.Chat.Id}生图模型设置成功，原模型：{previous}，现模型：{current}。消息来源：{telegramMessage.MessageId}");
                await SendMessageService.SendMessage($"生图模型设置成功，原模型：{previous}，现模型：{current}", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return;
            }

            if (Message.StartsWith("设置音乐模型 ") && isNormalAdmin) {
                var requestedModelName = Message.Substring(7).Trim();
                if (string.IsNullOrWhiteSpace(requestedModelName)) {
                    await SendMessageService.SendMessage("音乐模型名称不能为空", telegramMessage.Chat.Id, telegramMessage.MessageId);
                    return;
                }

                var (previous, current) = await _musicGenerationToolSettingsService.SetGroupModelNameAsync(telegramMessage.Chat.Id, requestedModelName);
                logger.LogInformation($"群{telegramMessage.Chat.Id}音乐模型设置成功，原模型：{previous}，现模型：{current}。消息来源：{telegramMessage.MessageId}");
                await SendMessageService.SendMessage($"音乐模型设置成功，原模型：{previous}，现模型：{current}", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return;
            }

            if (( Message.Equals("清除生图模型", StringComparison.OrdinalIgnoreCase) ||
                  Message.Equals("重置生图模型", StringComparison.OrdinalIgnoreCase) ) &&
                isNormalAdmin) {
                var defaultModel = await _imageGenerationToolSettingsService.ClearGroupModelNameAsync(telegramMessage.Chat.Id);
                logger.LogInformation($"群{telegramMessage.Chat.Id}生图模型已清除，将使用默认模型：{defaultModel}。消息来源：{telegramMessage.MessageId}");
                await SendMessageService.SendMessage($"生图模型已清除，当前会使用默认模型：{defaultModel}", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return;
            }

            if (( Message.Equals("清除音乐模型", StringComparison.OrdinalIgnoreCase) ||
                  Message.Equals("重置音乐模型", StringComparison.OrdinalIgnoreCase) ) &&
                isNormalAdmin) {
                var defaultModel = await _musicGenerationToolSettingsService.ClearGroupModelNameAsync(telegramMessage.Chat.Id);
                logger.LogInformation($"群{telegramMessage.Chat.Id}音乐模型已清除，将使用默认模型：{defaultModel}。消息来源：{telegramMessage.MessageId}");
                await SendMessageService.SendMessage($"音乐模型已清除，当前会使用默认模型：{defaultModel}", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return;
            }

            if (( Message.Equals("生图模型", StringComparison.OrdinalIgnoreCase) ||
                  Message.Equals("查看生图模型", StringComparison.OrdinalIgnoreCase) ) &&
                isNormalAdmin) {
                var modelName = await _imageGenerationToolSettingsService.GetModelNameAsync(telegramMessage.Chat.Id);
                await SendMessageService.SendMessage($"当前生图模型：{modelName}", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return;
            }

            if (( Message.Equals("音乐模型", StringComparison.OrdinalIgnoreCase) ||
                  Message.Equals("查看音乐模型", StringComparison.OrdinalIgnoreCase) ) &&
                isNormalAdmin) {
                var modelName = await _musicGenerationToolSettingsService.GetModelNameAsync(telegramMessage.Chat.Id);
                await SendMessageService.SendMessage($"当前音乐模型：{modelName}", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return;
            }

            if (Message.StartsWith("设置模型 ") && isNormalAdmin) {
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
                if (fromUserId != 0 && await _llmVisibilityService.IsUserInvisibleAsync(telegramMessage.Chat.Id, fromUserId)) {
                    logger.LogInformation("User {UserId} in chat {ChatId} is LLM invisible; skipping LLM execution for message {MessageId}.",
                        fromUserId, telegramMessage.Chat.Id, telegramMessage.MessageId);
                    await SendMessageService.SendMessage("你已开启 LLM 隐身，这条消息不会发送给 LLM。发送 `取消LLM隐身` 可恢复。", telegramMessage.Chat.Id, telegramMessage.MessageId);
                    return;
                }

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

        private async Task<bool> TryHandlePendingImageGenerationModelSelectionAsync(Telegram.Bot.Types.Message telegramMessage, string messageText, long userId) {
            var db = _connectionMultiplexer.GetDatabase();
            var key = GetImageGenerationModelSelectionKey(telegramMessage.Chat.Id, userId);
            var stored = await db.StringGetAsync(key);
            if (!stored.HasValue) {
                return false;
            }

            var trimmed = messageText.Trim();
            if (trimmed.Equals("取消", StringComparison.OrdinalIgnoreCase)) {
                await db.KeyDeleteAsync(key);
                await SendMessageService.SendMessage("已取消选择生图模型。", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return true;
            }

            if (!int.TryParse(trimmed, out var index)) {
                await SendMessageService.SendMessage("请输入生图模型编号，或发送 `取消`。", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return true;
            }

            var modelNames = stored.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (index < 1 || index > modelNames.Count) {
                await SendMessageService.SendMessage($"无效编号，请输入 1 到 {modelNames.Count} 之间的数字，或发送 `取消`。", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return true;
            }

            var modelName = modelNames[index - 1];
            var (previous, current) = await _imageGenerationToolSettingsService.SetGroupModelNameAsync(telegramMessage.Chat.Id, modelName);
            await db.KeyDeleteAsync(key);

            logger.LogInformation($"群{telegramMessage.Chat.Id}生图模型设置成功，原模型：{previous}，现模型：{current}。消息来源：{telegramMessage.MessageId}");
            await SendMessageService.SendMessage($"生图模型设置成功，原模型：{previous}，现模型：{current}", telegramMessage.Chat.Id, telegramMessage.MessageId);
            return true;
        }

        private async Task<bool> TryHandlePendingMusicGenerationModelSelectionAsync(Telegram.Bot.Types.Message telegramMessage, string messageText, long userId) {
            var db = _connectionMultiplexer.GetDatabase();
            var key = GetMusicGenerationModelSelectionKey(telegramMessage.Chat.Id, userId);
            var stored = await db.StringGetAsync(key);
            if (!stored.HasValue) {
                return false;
            }

            var trimmed = messageText.Trim();
            if (trimmed.Equals("取消", StringComparison.OrdinalIgnoreCase)) {
                await db.KeyDeleteAsync(key);
                await SendMessageService.SendMessage("已取消选择音乐模型。", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return true;
            }

            if (!int.TryParse(trimmed, out var index)) {
                await SendMessageService.SendMessage("请输入音乐模型编号，或发送 `取消`。", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return true;
            }

            var modelNames = stored.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (index < 1 || index > modelNames.Count) {
                await SendMessageService.SendMessage($"无效编号，请输入 1 到 {modelNames.Count} 之间的数字，或发送 `取消`。", telegramMessage.Chat.Id, telegramMessage.MessageId);
                return true;
            }

            var modelName = modelNames[index - 1];
            var (previous, current) = await _musicGenerationToolSettingsService.SetGroupModelNameAsync(telegramMessage.Chat.Id, modelName);
            await db.KeyDeleteAsync(key);

            logger.LogInformation($"群{telegramMessage.Chat.Id}音乐模型设置成功，原模型：{previous}，现模型：{current}。消息来源：{telegramMessage.MessageId}");
            await SendMessageService.SendMessage($"音乐模型设置成功，原模型：{previous}，现模型：{current}", telegramMessage.Chat.Id, telegramMessage.MessageId);
            return true;
        }

        private async Task<List<ImageGenerationModelSelectionOption>> LoadImageGenerationModelOptionsAsync() {
            var models = await _modelCapabilityService.GetImageGenerationModels();
            return models
                .Where(x => !string.IsNullOrWhiteSpace(x.ModelName) && x.LLMChannel != null)
                .GroupBy(x => x.ModelName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => {
                    var channels = group
                        .Select(x => $"{x.LLMChannel.Name}#{x.LLMChannel.Id}/{x.LLMChannel.Provider}")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();
                    var channelSummary = channels.Count <= 3
                        ? string.Join(", ", channels)
                        : $"{string.Join(", ", channels.Take(3))} 等 {channels.Count} 个渠道";
                    return new ImageGenerationModelSelectionOption(group.Key, channelSummary);
                })
                .OrderBy(x => x.ModelName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<MusicGenerationModelSelectionOption>> LoadMusicGenerationModelOptionsAsync() {
            var models = await _modelCapabilityService.GetMusicGenerationModels();
            return models
                .Where(x => !string.IsNullOrWhiteSpace(x.ModelName) && x.LLMChannel != null)
                .GroupBy(x => x.ModelName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => {
                    var channels = group
                        .Select(x => $"{x.LLMChannel.Name}#{x.LLMChannel.Id}/{x.LLMChannel.Provider}")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();
                    var channelSummary = channels.Count <= 3
                        ? string.Join(", ", channels)
                        : $"{string.Join(", ", channels.Take(3))} 等 {channels.Count} 个渠道";
                    return new MusicGenerationModelSelectionOption(group.Key, channelSummary);
                })
                .OrderBy(x => x.ModelName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task SaveImageGenerationModelSelectionAsync(long chatId, long userId, List<ImageGenerationModelSelectionOption> options) {
            var db = _connectionMultiplexer.GetDatabase();
            var key = GetImageGenerationModelSelectionKey(chatId, userId);
            var modelNames = options.Take(50).Select(x => x.ModelName);
            await db.StringSetAsync(key, string.Join('\n', modelNames), TimeSpan.FromMinutes(10));
        }

        private async Task SaveMusicGenerationModelSelectionAsync(long chatId, long userId, List<MusicGenerationModelSelectionOption> options) {
            var db = _connectionMultiplexer.GetDatabase();
            var key = GetMusicGenerationModelSelectionKey(chatId, userId);
            var modelNames = options.Take(50).Select(x => x.ModelName);
            await db.StringSetAsync(key, string.Join('\n', modelNames), TimeSpan.FromMinutes(10));
        }

        private static string BuildImageGenerationModelSelectionMessage(List<ImageGenerationModelSelectionOption> options) {
            var limited = options.Take(50).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("请选择当前群使用的生图模型，回复编号即可：");
            for (var i = 0; i < limited.Count; i++) {
                sb.AppendLine($"{i + 1}. {limited[i].ModelName} ({limited[i].ChannelSummary})");
            }

            if (options.Count > limited.Count) {
                sb.AppendLine($"仅显示前 {limited.Count} 个，共 {options.Count} 个。");
            }

            sb.AppendLine("发送 `取消` 可退出选择。");
            return sb.ToString();
        }

        private static string BuildMusicGenerationModelSelectionMessage(List<MusicGenerationModelSelectionOption> options) {
            var limited = options.Take(50).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("请选择当前群使用的音乐模型，回复编号即可：");
            for (var i = 0; i < limited.Count; i++) {
                sb.AppendLine($"{i + 1}. {limited[i].ModelName} ({limited[i].ChannelSummary})");
            }

            if (options.Count > limited.Count) {
                sb.AppendLine($"仅显示前 {limited.Count} 个，共 {options.Count} 个。");
            }

            sb.AppendLine("发送 `取消` 可退出选择。");
            return sb.ToString();
        }

        private static string GetImageGenerationModelSelectionKey(long chatId, long userId) {
            return LlmAgentRedisKeys.ImageGenerationModelSelection(chatId, userId);
        }

        private static string GetMusicGenerationModelSelectionKey(long chatId, long userId) {
            return LlmAgentRedisKeys.MusicGenerationModelSelection(chatId, userId);
        }

        private sealed record ImageGenerationModelSelectionOption(string ModelName, string ChannelSummary);
        private sealed record MusicGenerationModelSelectionOption(string ModelName, string ChannelSummary);
    }
}

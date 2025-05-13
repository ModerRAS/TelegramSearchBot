using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types; // For Message
using Telegram.Bot.Types.Enums; // Added for ChatType
using TelegramSearchBot.Interfaces; // For ICommandParsingGrain, ITelegramMessageSenderGrain
using TelegramSearchBot.Model;    // For StreamMessage, OrleansStreamConstants

namespace TelegramSearchBot.Grains
{
    public class CommandParsingGrain : Grain, ICommandParsingGrain, IAsyncObserver<StreamMessage<Message>>
    {
        private readonly IGrainFactory _grainFactory;
        private readonly ILogger _logger;
        private readonly IGroupSettingsService _groupSettingsService;
        private readonly TelegramSearchBot.Service.Common.IAppConfigurationService _appConfigurationService; // Added
        private IAsyncStream<StreamMessage<Message>> _commandStream;

        public CommandParsingGrain(
            IGrainFactory grainFactory, 
            ILogger logger,
            IGroupSettingsService groupSettingsService,
            TelegramSearchBot.Service.Common.IAppConfigurationService appConfigurationService) // Added
        {
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _logger = logger?.ForContext<CommandParsingGrain>() ?? Log.ForContext<CommandParsingGrain>(); 
            _groupSettingsService = groupSettingsService ?? throw new ArgumentNullException(nameof(groupSettingsService));
            _appConfigurationService = appConfigurationService ?? throw new ArgumentNullException(nameof(appConfigurationService)); // Added
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("CommandParsingGrain {GrainId} activated.", this.GetGrainId());

            var streamProvider = this.GetStreamProvider("DefaultSMSProvider"); // Assuming "DefaultSMSProvider"
            _commandStream = streamProvider.GetStream<StreamMessage<Message>>(
                OrleansStreamConstants.RawCommandMessagesStreamName,
                OrleansStreamConstants.RawMessagesStreamNamespace);
            
            await _commandStream.SubscribeAsync(this);
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task OnNextAsync(StreamMessage<Message> streamMessage, StreamSequenceToken token = null)
        {
            var originalMessage = streamMessage.Payload;
            if (originalMessage?.Text == null)
            {
                _logger.Warning("CommandParsingGrain received message without text. MessageId: {MessageId}", originalMessage?.MessageId);
                return;
            }

            _logger.Information("CommandParsingGrain received command: \"{CommandText}\" from ChatId: {ChatId}, MessageId: {MessageId}",
                originalMessage.Text, originalMessage.Chat.Id, originalMessage.MessageId);

            var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0); // Assuming stateless worker or well-known ID

            try
            {
                string commandText = originalMessage.Text.Trim();
                string command = string.Empty;
                string args = string.Empty;

                // Check for "搜索 " command first
                if (commandText.StartsWith("搜索 ") && commandText.Length > "搜索 ".Length)
                {
                    command = "搜索"; // Normalized command key
                    args = commandText.Substring("搜索 ".Length).Trim();
                }
                else if (commandText.StartsWith("/")) // Handle slash commands
                {
                    string[] slashCommandParts = commandText.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    command = slashCommandParts.Length > 0 ? slashCommandParts[0].ToLowerInvariant() : string.Empty;
                    args = slashCommandParts.Length > 1 ? slashCommandParts[1] : string.Empty;
                }
                // Else, it's not a recognized command format for this grain 
                // if it doesn't start with / and isn't "搜索 ".
                // The switch statement's default case will handle or log this.

                string responseText = null; 
                bool commandProcessedByThisGrain = false; // Indicates if this grain will send a direct reply

                switch (command)
                {
                    case "/start":
                        responseText = "欢迎使用TelegramSearchBot！输入 /help 查看所有可用指令。";
                        commandProcessedByThisGrain = true;
                        break;
                    case "/help":
                        responseText = "【通用用户指令】\n" +
                            "搜索 <关键词> - 搜索内容\n" +
                            "/resolveurls <文本或回复消息> - 解析短链\n" +
                            "\n【管理员指令】\n" +
                            "设置模型 <模型名称>\n" +
                            "设置管理群/取消管理群\n" +
                            "/setbilicookie <Cookie字符串>\n" +
                            "/getbilicookie\n" +
                            "/setbilimaxsize <MB数>\n" +
                            "/getbilimaxsize\n" +
                            "新建渠道/编辑渠道/添加模型/移除模型/查看模型\n" +
                            "重建索引/导入数据/迁移数据\n" +
                            "\n详细说明请见用户指南。";
                        commandProcessedByThisGrain = true;
                        break;
                    case "搜索": // Handles "搜索 <关键词>"
                        if (string.IsNullOrWhiteSpace(args))
                        {
                            responseText = "请输入搜索关键词。用法: 搜索 <关键词>";
                            commandProcessedByThisGrain = true;
                        }
                        else
                        {
                            // Generate a unique ID for the search session
                            // Using ChatId and UserId in the key helps if multiple users in the same chat search simultaneously,
                            // or one user has multiple search sessions. Guid ensures uniqueness.
                            string searchQueryGrainId = $"search_{originalMessage.Chat.Id}_{originalMessage.From.Id}_{Guid.NewGuid().ToString("N").Substring(0,12)}";
                            var searchQueryGrain = _grainFactory.GetGrain<ISearchQueryGrain>(searchQueryGrainId);
                            
                            _logger.Information("Activating SearchQueryGrain {SearchQueryGrainId} for query \"{Query}\" from User {UserId} in Chat {ChatId}",
                                searchQueryGrainId, args, originalMessage.From.Id, originalMessage.Chat.Id);

                            await searchQueryGrain.StartSearchAsync(args, originalMessage.Chat.Id, originalMessage.MessageId, originalMessage.From.Id);
                            // SearchQueryGrain handles its own responses.
                        }
                        // commandProcessedByThisGrain remains false if search is offloaded, unless args were empty.
                        if (string.IsNullOrWhiteSpace(args)) commandProcessedByThisGrain = true; 
                        break;
                    case "/resolveurls":
                        string textToParse = args;
                        if (string.IsNullOrWhiteSpace(textToParse) && originalMessage.ReplyToMessage != null)
                        {
                            textToParse = originalMessage.ReplyToMessage.Text ?? originalMessage.ReplyToMessage.Caption;
                        }

                        if (string.IsNullOrWhiteSpace(textToParse))
                        {
                            responseText = "请提供包含URL的文本，或回复一条包含URL的消息。\n用法: /resolveurls <文本> 或 回复消息后输入 /resolveurls";
                        }
                        else
                        {
                            // UrlExtractionGrain uses IGrainWithGuidKey
                            var urlExtractionGrain = _grainFactory.GetGrain<IUrlExtractionGrain>(Guid.NewGuid());
                            responseText = await urlExtractionGrain.GetFormattedResolvedUrlsAsync(textToParse);
                        }
                        commandProcessedByThisGrain = true;
                        break;
                    case "设置模型": // Command in Chinese as per user guide
                    case "/setllmmodel": // Optional English alias
                        if (originalMessage.Chat.Type == ChatType.Private)
                        {
                            responseText = "此命令只能在群组或频道中使用。";
                        }
                        else
                        {
                            bool isAdmin = await _groupSettingsService.IsUserChatAdminAsync(originalMessage.Chat.Id, originalMessage.From.Id);
                            if (!isAdmin)
                            {
                                responseText = "抱歉，只有聊天管理员才能设置默认LLM模型。";
                            }
                            else if (string.IsNullOrWhiteSpace(args))
                            {
                                responseText = "请提供模型名称。用法: 设置模型 <模型友好名称>";
                            }
                            else
                            {
                                string modelName = args.Trim();
                                // TODO: Potentially validate modelName against a list of known/available models.
                                // For now, directly set it.
                                await _groupSettingsService.SetLlmModelForChatAsync(originalMessage.Chat.Id, modelName);
                                responseText = $"当前聊天的默认LLM模型已设置为: {modelName}";
                                _logger.Information("User {UserId} set LLM model for chat {ChatId} to {ModelName}", 
                                    originalMessage.From.Id, originalMessage.Chat.Id, modelName);
                            }
                        }
                        commandProcessedByThisGrain = true;
                        break;
                    case "/getchatid":
                        responseText = $"当前会话ID: `{originalMessage.Chat.Id}`";
                        commandProcessedByThisGrain = true;
                        break;
                    case "/getappsettings":
                        if (string.IsNullOrWhiteSpace(args))
                        {
                            responseText = "请提供配置项的Key。用法: /getappsettings <key>";
                        }
                        else
                        {
                            // This command should ideally be admin-only.
                            // Using IGroupSettingsService for admin check, assuming IAppConfigurationService is also admin-protected or for global settings.
                            bool isCmdAdmin = await _groupSettingsService.IsUserChatAdminAsync(originalMessage.Chat.Id, originalMessage.From.Id);
                            if (!isCmdAdmin && originalMessage.Chat.Type != ChatType.Private) // Allow in private chat for self-testing by bot owner if needed, or restrict further.
                            {
                                // A more robust global admin check might be needed if settings are sensitive.
                                // For now, restricting to chat admin for group settings.
                                // If IAppConfigurationService settings are global, a global admin check is better.
                                // Let's assume for now it's for chat admins or bot owner in private.
                                // This part needs clarification on who can get/set AppSettings.
                                // For now, let's make it chat admin only in groups.
                                responseText = "抱歉，只有聊天管理员才能获取应用设置。";
                            }
                            else
                            {
                                // Use the injected _appConfigurationService directly
                                string configKey = args.Trim();
                                string configValue = await _appConfigurationService.GetConfigurationValueAsync(configKey);
                                responseText = string.IsNullOrEmpty(configValue) ? $"未找到配置项: `{configKey}`" : $"配置项 `{configKey}` 的值为: `{configValue}`";
                            }
                        }
                        commandProcessedByThisGrain = true;
                        break;
                    case "/setappsettings":
                        string[] setParts = args.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (setParts.Length < 2)
                        {
                            responseText = "参数不足。用法: /setappsettings <key> <value>";
                        }
                        else
                        {
                            bool isCmdAdmin = await _groupSettingsService.IsUserChatAdminAsync(originalMessage.Chat.Id, originalMessage.From.Id);
                             if (!isCmdAdmin && originalMessage.Chat.Type != ChatType.Private)
                            {
                                responseText = "抱歉，只有聊天管理员才能设置应用配置。";
                            }
                            else
                            {
                                // Use the injected _appConfigurationService directly
                                string configKey = setParts[0].Trim();
                                string configValue = setParts[1].Trim();
                                await _appConfigurationService.SetConfigurationValueAsync(configKey, configValue);
                                responseText = $"配置项 `{configKey}` 已设置为: `{configValue}`";
                            }
                        }
                        commandProcessedByThisGrain = true;
                        break;
                    case "/setbilimaxsize":
                        if (Env.AdminId != originalMessage.From.Id)
                        {
                            responseText = "只有全局管理员可以设置B站最大下载大小。";
                        }
                        else
                        {
                            // TODO: 设置B站最大下载大小逻辑
                            responseText = "[TODO] 设置B站最大下载大小逻辑未实现";
                        }
                        commandProcessedByThisGrain = true;
                        break;
                    case "设置B站最大下载大小":
                        goto case "/setbilimaxsize";
                    case "/getbilimaxsize":
                        if (Env.AdminId != originalMessage.From.Id)
                        {
                            responseText = "只有全局管理员可以获取B站最大下载大小。";
                        }
                        else
                        {
                            // TODO: 获取B站最大下载大小逻辑
                            responseText = "[TODO] 获取B站最大下载大小逻辑未实现";
                        }
                        commandProcessedByThisGrain = true;
                        break;
                    case "获取B站最大下载大小":
                        goto case "/getbilimaxsize";
                    case "设置管理群":
                        if (Env.AdminId != originalMessage.From.Id)
                        {
                            responseText = "只有全局管理员可以设置管理群。";
                            commandProcessedByThisGrain = true;
                        }
                        else
                        {
                            // TODO: 设置管理群逻辑
                            responseText = "[TODO] 设置管理群逻辑未实现";
                            commandProcessedByThisGrain = true;
                        }
                        break;
                    case "取消管理群":
                        if (Env.AdminId != originalMessage.From.Id)
                        {
                            responseText = "只有全局管理员可以取消管理群。";
                            commandProcessedByThisGrain = true;
                        }
                        else
                        {
                            // TODO: 取消管理群逻辑
                            responseText = "[TODO] 取消管理群逻辑未实现";
                            commandProcessedByThisGrain = true;
                        }
                        break;
                    case "/setbilicookie":
                    case "设置B站Cookie":
                        if (Env.AdminId != originalMessage.From.Id)
                        {
                            responseText = "只有全局管理员可以设置B站Cookie。";
                        }
                        else
                        {
                            // TODO: 设置B站Cookie逻辑
                            responseText = "[TODO] 设置B站Cookie逻辑未实现";
                        }
                        commandProcessedByThisGrain = true;
                        break;
                    case "/getbilicookie":
                    case "获取B站Cookie":
                        if (Env.AdminId != originalMessage.From.Id)
                        {
                            responseText = "只有全局管理员可以获取B站Cookie。";
                        }
                        else
                        {
                            // TODO: 获取B站Cookie逻辑
                            responseText = "[TODO] 获取B站Cookie逻辑未实现";
                        }
                        commandProcessedByThisGrain = true;
                        break;
                    case "新建渠道":
                    case "编辑渠道":
                    case "添加模型":
                    case "移除模型":
                    case "查看模型":
                        if (Env.AdminId != originalMessage.From.Id)
                        {
                            responseText = "只有全局管理员可以管理LLM渠道。";
                        }
                        else
                        {
                            // TODO: LLM渠道管理状态机逻辑
                            responseText = "[TODO] LLM渠道管理逻辑未实现";
                        }
                        commandProcessedByThisGrain = true;
                        break;
                    case "重建索引":
                    case "导入数据":
                    case "迁移数据":
                        if (Env.AdminId != originalMessage.From.Id)
                        {
                            responseText = "只有全局管理员可以执行此操作。";
                        }
                        else
                        {
                            // TODO: 数据管理逻辑
                            responseText = "[TODO] 数据管理逻辑未实现";
                        }
                        commandProcessedByThisGrain = true;
                        break;
                    default:
                        if (command.StartsWith("/"))
                        {
                            responseText = $"未知的命令: {command}\n输入 /help 查看可用命令。";
                            commandProcessedByThisGrain = true;
                        }
                        else if (string.IsNullOrEmpty(command) && !string.IsNullOrWhiteSpace(commandText))
                        {
                            _logger.Debug("CommandParsingGrain received text not matching known command patterns: {FullText}", commandText);
                            return;
                        } else if (string.IsNullOrEmpty(command)) {
                            return;
                        }
                        break;
                }
                
                if (commandProcessedByThisGrain && !string.IsNullOrEmpty(responseText))
                {
                    await senderGrain.SendMessageAsync(new TelegramMessageToSend
                    {
                        ChatId = originalMessage.Chat.Id,
                        Text = responseText,
                        ReplyToMessageId = originalMessage.MessageId
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing command \"{CommandText}\" for MessageId {MessageId}", originalMessage.Text, originalMessage.MessageId);
                await senderGrain.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = originalMessage.Chat.Id,
                    Text = "处理您的命令时发生内部错误。",
                    ReplyToMessageId = originalMessage.MessageId
                });
            }
        }

        public Task OnCompletedAsync()
        {
            _logger.Information("CommandParsingGrain {GrainId} completed stream processing.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.Error(ex, "CommandParsingGrain {GrainId} encountered an error on stream.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.Information("CommandParsingGrain {GrainId} deactivating. Reason: {Reason}", this.GetGrainId(), reason);
            if (_commandStream != null)
            {
                var subscriptions = await _commandStream.GetAllSubscriptionHandles();
                foreach (var sub in subscriptions)
                {
                    await sub.UnsubscribeAsync();
                }
            }
            await base.OnDeactivateAsync(reason, cancellationToken);
        }
    }
}

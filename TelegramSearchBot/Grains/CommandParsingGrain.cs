using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types; // For Message
using TelegramSearchBot.Interfaces; // For ICommandParsingGrain, ITelegramMessageSenderGrain
using TelegramSearchBot.Model;    // For StreamMessage, OrleansStreamConstants

namespace TelegramSearchBot.Grains
{
    public class CommandParsingGrain : Grain, ICommandParsingGrain, IAsyncObserver<StreamMessage<Message>>
    {
        private readonly IGrainFactory _grainFactory;
        private readonly ILogger _logger;
        private IAsyncStream<StreamMessage<Message>> _commandStream;

        public CommandParsingGrain(IGrainFactory grainFactory, ILogger logger) // ILogger can be injected directly in Orleans 7+
        {
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _logger = logger?.ForContext<CommandParsingGrain>() ?? Log.ForContext<CommandParsingGrain>(); // Ensure logger is not null
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
                string[] parts = commandText.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
                string args = parts.Length > 1 ? parts[1] : string.Empty;

                // TODO: Implement more command dispatch logic (switch statement or dictionary-based)
                string responseText = null; // Initialize to null
                bool commandHandled = false; // Flag to check if a command was specifically handled

                switch (command)
                {
                    case "/start":
                        responseText = "欢迎使用机器人！这是一个 /start 命令的响应。";
                        commandHandled = true;
                        break;
                    case "/help":
                        responseText = "可用的命令：\n/start - 开始\n/help - 显示此帮助信息\n/search <关键词> - 搜索内容\n（更多命令待实现）";
                        commandHandled = true;
                        break;
                    case "/search":
                    case "/s": // Alias for search
                        if (string.IsNullOrWhiteSpace(args))
                        {
                            responseText = "请输入搜索关键词。用法: /search <关键词>";
                        }
                        else
                        {
                            // Generate a unique ID for the search session
                            string searchQueryGrainId = $"search_{originalMessage.Chat.Id}_{originalMessage.From.Id}_{Guid.NewGuid().ToString().Substring(0, 8)}";
                            var searchQueryGrain = _grainFactory.GetGrain<ISearchQueryGrain>(searchQueryGrainId);
                            
                            _logger.Information("Activating SearchQueryGrain {SearchQueryGrainId} for query \"{Query}\" from User {UserId} in Chat {ChatId}",
                                searchQueryGrainId, args, originalMessage.From.Id, originalMessage.Chat.Id);

                            // Offload the actual search and response to SearchQueryGrain
                            // CommandParsingGrain will not send a direct response here.
                            // SearchQueryGrain will handle sending "Searching..." message, results, and pagination.
                            await searchQueryGrain.StartSearchAsync(args, originalMessage.Chat.Id, originalMessage.MessageId, originalMessage.From.Id);
                            // No responseText needed from CommandParsingGrain for /search, as SearchQueryGrain handles it.
                        }
                        commandHandled = true; // Mark as handled even if args are empty (we sent a usage message) or offloaded
                        break;
                    // Add more command cases here
                    default:
                        if (command.StartsWith("/"))
                        {
                            responseText = $"未知的命令: {command}\n输入 /help 查看可用命令。";
                        }
                        else
                        {
                            _logger.Warning("CommandParsingGrain received non-command text (should not happen on RawCommandMessagesStream): {FullText}", commandText);
                            return; 
                        }
                        commandHandled = true; // Also handled by sending "unknown command"
                        break;
                }
                
                // Send a response only if responseText is set (i.e., for simple commands handled directly here)
                if (!string.IsNullOrEmpty(responseText))
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

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

                // TODO: Implement command dispatch logic (switch statement or dictionary-based)
                // For now, just acknowledge the command or send help.
                string responseText;
                switch (command)
                {
                    case "/start":
                        responseText = "欢迎使用机器人！这是一个 /start 命令的响应。";
                        break;
                    case "/help":
                        responseText = "可用的命令：\n/start - 开始\n/help - 显示此帮助信息\n（更多命令待实现）";
                        break;
                    // Add more command cases here
                    // e.g., case "/settings":
                    // var settingsGrain = _grainFactory.GetGrain<ISettingsCommandGrain>(0); // Example
                    // responseText = await settingsGrain.HandleSettingsAsync(args, streamMessage.ChatId);
                    // break;
                    default:
                        if (command.StartsWith("/"))
                        {
                            responseText = $"未知的命令: {command}\n输入 /help 查看可用命令。";
                        }
                        else
                        {
                            // This case should ideally not happen if this grain only subscribes to RawCommandMessagesStreamName
                            // which should only contain messages starting with "/"
                            _logger.Warning("CommandParsingGrain received non-command text: {FullText}", commandText);
                            return; // Or handle as an error/ignore
                        }
                        break;
                }
                
                await senderGrain.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = originalMessage.Chat.Id,
                    Text = responseText,
                    ReplyToMessageId = originalMessage.MessageId
                });
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

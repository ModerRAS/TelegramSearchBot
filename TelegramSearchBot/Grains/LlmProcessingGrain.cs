using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Interfaces;
using TelegramSearchBot.Model;    // For StreamMessage, OrleansStreamConstants
using TelegramSearchBot.Service.AI.LLM; // For GeneralLLMService
using TelegramSearchBot.Service.BotAPI; // For SendMessageService
using TelegramSearchBot.Service.Storage; // For MessageService

namespace TelegramSearchBot.Grains
{
    public class LlmProcessingGrain : Grain, ILlmProcessingGrain, IAsyncObserver<StreamMessage<Message>>
    {
        private readonly GeneralLLMService _generalLlmService;
        private readonly SendMessageService _sendMessageService;
        private readonly MessageService _messageService;
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger _logger;
        private readonly IGrainFactory _grainFactory; // To get ITelegramMessageSenderGrain for simple error messages

        private IAsyncStream<StreamMessage<Message>> _llmTriggerStreamSubscription;
        
        // These would ideally come from a configuration service or be initialized at startup
        private long _botId;
        private string _botUsername;


        public LlmProcessingGrain(
            GeneralLLMService generalLlmService,
            SendMessageService sendMessageService, // Assuming this can be injected
            MessageService messageService,         // Assuming this can be injected
            ITelegramBotClient botClient,
            IGrainFactory grainFactory)
        {
            _generalLlmService = generalLlmService ?? throw new ArgumentNullException(nameof(generalLlmService));
            _sendMessageService = sendMessageService ?? throw new ArgumentNullException(nameof(sendMessageService));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _logger = Log.ForContext<LlmProcessingGrain>();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("LlmProcessingGrain {GrainId} activated.", this.GetGrainId());

            try
            {
                var me = await _botClient.GetMeAsync(cancellationToken);
                _botId = me.Id;
                _botUsername = me.Username;
                _logger.Information("LlmProcessingGrain: Bot ID {BotId}, Username {BotUsername} fetched.", _botId, _botUsername);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "LlmProcessingGrain: Failed to get bot info on activation.");
                // Depending on requirements, might prevent subscription or allow graceful degradation
            }
            
            var streamProvider = this.GetStreamProvider("DefaultSMSProvider");

            // Subscribe to the new stream for LLM triggers
            // Assuming OrleansStreamConstants.LlmInteractionTriggerStreamName and OrleansStreamConstants.LlmStreamNamespace are defined
            _llmTriggerStreamSubscription = streamProvider.GetStream<StreamMessage<Message>>(
                OrleansStreamConstants.LlmInteractionTriggerStreamName, // Placeholder name
                OrleansStreamConstants.LlmStreamNamespace);             // Placeholder name
            
            await _llmTriggerStreamSubscription.SubscribeAsync(this);

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task OnNextAsync(StreamMessage<Message> streamMessage, StreamSequenceToken token = null)
        {
            var originalMessage = streamMessage.Payload;
            if (originalMessage == null) return;

            if (_botId == 0 || string.IsNullOrEmpty(_botUsername))
            {
                _logger.Warning("LlmProcessingGrain: Bot ID or Username not initialized. Cannot process LLM trigger for MessageId: {MessageId}", originalMessage.MessageId);
                // Attempt to re-fetch bot info, or fail gracefully
                try {
                    var me = await _botClient.GetMeAsync();
                    _botId = me.Id;
                    _botUsername = me.Username;
                    if (_botId == 0 || string.IsNullOrEmpty(_botUsername)) throw new Exception("Still couldn't get bot info.");
                } catch (Exception ex) {
                    _logger.Error(ex, "LlmProcessingGrain: Critical - failed to get bot info during message processing.");
                    return;
                }
            }

            string prompt = originalMessage.Text ?? originalMessage.Caption;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                _logger.Information("LlmProcessingGrain: Empty text/caption for LLM processing. MessageId: {MessageId}", originalMessage.MessageId);
                return;
            }

            // Clean prompt if it's a mention
            if (originalMessage.Entities != null && originalMessage.Entities.Any(e => e.Type == MessageEntityType.Mention))
            {
                if (prompt.Contains($"@{_botUsername}"))
                {
                    prompt = prompt.Replace($"@{_botUsername}", "").Trim();
                }
            }
            
            // If after cleaning, prompt is empty (e.g., message was just "@BotName"), ignore or send help.
            if (string.IsNullOrWhiteSpace(prompt))
            {
                _logger.Information("LlmProcessingGrain: Prompt became empty after cleaning mention. MessageId: {MessageId}", originalMessage.MessageId);
                // Optionally, send a "how can I help?" message
                return;
            }

            _logger.Information("LlmProcessingGrain received LLM prompt: \"{Prompt}\" from OriginalMessageId: {OriginalMessageId}, ChatId: {ChatId}", 
                prompt, originalMessage.MessageId, originalMessage.Chat.Id);

            var senderGrainOnError = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);

            try
            {
                var llmInputMessage = new TelegramSearchBot.Model.Data.Message // This is our DB model
                {
                    Content = prompt,
                    DateTime = originalMessage.Date.ToUniversalTime(), // Ensure UTC DateTime
                    FromUserId = originalMessage.From.Id,
                    GroupId = originalMessage.Chat.Id,
                    MessageId = originalMessage.MessageId, 
                    ReplyToMessageId = originalMessage.ReplyToMessage?.MessageId ?? 0,
                    Id = -1, // DB will assign
                };
                
                // Store the user's prompt message
                await _messageService.ExecuteAsync(new MessageOption
                {
                    Chat = originalMessage.Chat,
                    ChatId = llmInputMessage.GroupId,
                    Content = llmInputMessage.Content,
                    DateTime = llmInputMessage.DateTime,
                    MessageId = llmInputMessage.MessageId,
                    User = originalMessage.From,
                    ReplyTo = llmInputMessage.ReplyToMessageId,
                    UserId = llmInputMessage.FromUserId,
                });

                var initialContentPlaceholder = "思考中..."; // Temporarily using a generic placeholder

                IAsyncEnumerable<string> fullMessageStream = _generalLlmService.ExecAsync(llmInputMessage, originalMessage.Chat.Id, CancellationToken.None);

                List<TelegramSearchBot.Model.Data.Message> sentBotMessages = await _sendMessageService.SendFullMessageStream(
                    fullMessageStream,
                    originalMessage.Chat.Id,
                    originalMessage.MessageId, 
                    initialContentPlaceholder,
                    CancellationToken.None
                );

                // Store the bot's responses
                var botUser = await _botClient.GetMeAsync(); // Get bot's User object for MessageOption
                foreach (var botMsg in sentBotMessages)
                {
                    // botMsg is already Model.Data.Message, correctly populated by SendFullMessageStream
                    await _messageService.ExecuteAsync(new MessageOption
                    {
                        ChatId = botMsg.GroupId,
                        Content = botMsg.Content,
                        DateTime = botMsg.DateTime,
                        MessageId = botMsg.MessageId,
                        User = botUser, // Use the bot's User object
                        ReplyTo = botMsg.ReplyToMessageId,
                        UserId = botMsg.FromUserId, // This is bot's ID
                        Chat = originalMessage.Chat // Use original chat context for consistency if needed
                    });
                }
                _logger.Information("LLM response stream processed and stored for OriginalMessageId: {OriginalMessageId}", originalMessage.MessageId);

            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during LLM processing for prompt: \"{Prompt}\", OriginalMessageId: {OriginalMessageId}", prompt, originalMessage.MessageId);
                await senderGrainOnError.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = originalMessage.Chat.Id,
                    Text = $"与AI助手交流时出错: {ex.Message}",
                    ReplyToMessageId = originalMessage.MessageId
                });
            }
        }

        public Task OnCompletedAsync()
        {
            _logger.Information("LlmProcessingGrain {GrainId} completed stream processing.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.Error(ex, "LlmProcessingGrain {GrainId} encountered an error on stream.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.Information("LlmProcessingGrain {GrainId} deactivating. Reason: {Reason}", this.GetGrainId(), reason);
            if (_llmTriggerStreamSubscription != null)
            {
                var subscriptions = await _llmTriggerStreamSubscription.GetAllSubscriptionHandles();
                foreach (var sub in subscriptions)
                {
                    await sub.UnsubscribeAsync();
                }
            }
            await base.OnDeactivateAsync(reason, cancellationToken);
        }
    }
}

using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.Collections.Generic; // For List<T>
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Interfaces;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.LLM; // For GeneralLLMService

namespace TelegramSearchBot.Grains
{
    public class LlmProcessingGrain : Grain, ILlmProcessingGrain, IAsyncObserver<StreamMessage<string>>
    {
        private readonly GeneralLLMService _llmService;
        private readonly ILogger _logger;
        private readonly IGrainFactory _grainFactory;

        private IAsyncStream<StreamMessage<string>> _textContentStreamSubscription;
        private const string LlmCommandPrefix = "/ask "; // Example command prefix

        public LlmProcessingGrain(GeneralLLMService llmService, IGrainFactory grainFactory)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _logger = Log.ForContext<LlmProcessingGrain>();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("LlmProcessingGrain {GrainId} activated.", this.GetGrainId());

            var streamProvider = this.GetStreamProvider("DefaultSMSProvider");

            _textContentStreamSubscription = streamProvider.GetStream<StreamMessage<string>>(
                OrleansStreamConstants.TextContentToProcessStreamName,
                OrleansStreamConstants.TextContentStreamNamespace);
            await _textContentStreamSubscription.SubscribeAsync(this);

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task OnNextAsync(StreamMessage<string> streamMessage, StreamSequenceToken token = null)
        {
            var textContent = streamMessage.Payload;
            if (string.IsNullOrWhiteSpace(textContent))
            {
                // _logger.Verbose("LlmProcessingGrain received empty or null text content. OriginalMessageId: {OriginalMessageId}", streamMessage.OriginalMessageId);
                return; // Ignore empty content
            }

            // Check for trigger condition (e.g., command prefix)
            if (!textContent.StartsWith(LlmCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // _logger.Verbose("LlmProcessingGrain: Text content from OriginalMessageId {OriginalMessageId} did not match LLM trigger.", streamMessage.OriginalMessageId);
                return; // Not an LLM command
            }

            string prompt = textContent.Substring(LlmCommandPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                _logger.Information("LlmProcessingGrain: Empty prompt after removing prefix from OriginalMessageId {OriginalMessageId}", streamMessage.OriginalMessageId);
                // Optionally send a "Please provide a prompt" message
                return;
            }

            _logger.Information("LlmProcessingGrain received LLM prompt: \"{Prompt}\" from OriginalMessageId: {OriginalMessageId}", prompt, streamMessage.OriginalMessageId);

            var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);

            try
            {
                // Construct a Model.Data.Message object for GeneralLLMService
                var llmInputMessage = new TelegramSearchBot.Model.Data.Message
                {
                    // Assuming Model.Data.Message has these properties.
                    // We might need to fetch the full original Telegram.Bot.Types.Message 
                    // from a storage service using streamMessage.OriginalMessageId if more fields are needed.
                // For now, using available info.
                MessageId = streamMessage.OriginalMessageId,
                GroupId = streamMessage.ChatId, // Changed ChatId to GroupId
                FromUserId = streamMessage.UserId, // Changed FromId to FromUserId
                DateTime = streamMessage.Timestamp, // Changed Date to DateTime
                Content = prompt, // Changed Text to Content
                // Other fields like Type, MediaGroupId etc., would be null or default if not directly available
                // from the StreamMessage<string> which only carries processed text.
            };

            var responseParts = new List<string>();
            // Pass the CancellationToken from OnActivateAsync or a new one if appropriate
            await foreach (var part in _llmService.ExecAsync(llmInputMessage, streamMessage.ChatId, CancellationToken.None).WithCancellation(CancellationToken.None))
            {
                responseParts.Add(part);
            }
            string llmResponse = string.Join("", responseParts);

            if (!string.IsNullOrWhiteSpace(llmResponse))
                {
                    await senderGrain.SendMessageAsync(new TelegramMessageToSend
                    {
                        ChatId = streamMessage.ChatId,
                        Text = llmResponse,
                        ReplyToMessageId = (int)streamMessage.OriginalMessageId
                    });
                    _logger.Information("LLM response sent for OriginalMessageId: {OriginalMessageId}", streamMessage.OriginalMessageId);
                }
                else
                {
                    _logger.Warning("LLM returned empty or null response for prompt: \"{Prompt}\", OriginalMessageId: {OriginalMessageId}", prompt, streamMessage.OriginalMessageId);
                    await senderGrain.SendMessageAsync(new TelegramMessageToSend
                    {
                        ChatId = streamMessage.ChatId,
                        Text = "抱歉，我无法处理您的请求或模型没有返回内容。",
                        ReplyToMessageId = (int)streamMessage.OriginalMessageId
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during LLM processing for prompt: \"{Prompt}\", OriginalMessageId: {OriginalMessageId}", prompt, streamMessage.OriginalMessageId);
                await senderGrain.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = streamMessage.ChatId,
                    Text = $"调用AI模型时出错: {ex.Message}",
                    ReplyToMessageId = (int)streamMessage.OriginalMessageId
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
            if (_textContentStreamSubscription != null)
            {
                var subscriptions = await _textContentStreamSubscription.GetAllSubscriptionHandles();
                foreach (var sub in subscriptions)
                {
                    await sub.UnsubscribeAsync();
                }
            }
            await base.OnDeactivateAsync(reason, cancellationToken);
        }
    }
}

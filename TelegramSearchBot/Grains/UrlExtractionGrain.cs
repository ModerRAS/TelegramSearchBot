using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Interfaces;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Common; // For UrlProcessingService

namespace TelegramSearchBot.Grains
{
    public class UrlExtractionGrain : Grain, IUrlExtractionGrain, IAsyncObserver<StreamMessage<string>>
    {
        private readonly UrlProcessingService _urlProcessingService;
        private readonly ILogger _logger;
        private readonly IGrainFactory _grainFactory; // To get ITelegramMessageSenderGrain

        private IAsyncStream<StreamMessage<string>> _textContentStreamSubscription;
        // private IAsyncStream<StreamMessage<ProcessedUrlInfo>> _processedUrlStream; // Optional: For further processing

        public UrlExtractionGrain(UrlProcessingService urlProcessingService, IGrainFactory grainFactory)
        {
            _urlProcessingService = urlProcessingService ?? throw new ArgumentNullException(nameof(urlProcessingService));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _logger = Log.ForContext<UrlExtractionGrain>();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("UrlExtractionGrain {GrainId} activated.", this.GetGrainId());

            var streamProvider = this.GetStreamProvider("DefaultSMSProvider");

            _textContentStreamSubscription = streamProvider.GetStream<StreamMessage<string>>(
                OrleansStreamConstants.TextContentToProcessStreamName,
                OrleansStreamConstants.TextContentStreamNamespace);
            await _textContentStreamSubscription.SubscribeAsync(this);

            // Example: If we were to publish processed URLs to another stream
            // _processedUrlStream = streamProvider.GetStream<StreamMessage<ProcessedUrlInfo>>(
            //     "ProcessedUrlsStreamName", // Define in OrleansStreamConstants
            //     "ProcessedContent");      // Define in OrleansStreamConstants

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task OnNextAsync(StreamMessage<string> streamMessage, StreamSequenceToken token = null)
        {
            var textContent = streamMessage.Payload;
            if (string.IsNullOrWhiteSpace(textContent))
            {
                _logger.Warning("UrlExtractionGrain received empty or null text content. OriginalMessageId: {OriginalMessageId}", streamMessage.OriginalMessageId);
                return;
            }

            _logger.Information("UrlExtractionGrain received text content from OriginalMessageId: {OriginalMessageId}", streamMessage.OriginalMessageId);

            try
            {
                // Use UrlProcessingService to extract URLs and potentially get titles.
                // The exact methods depend on UrlProcessingService's API.
                // Let's assume it has a method like: GetProcessedUrlsAsync(string text) -> List<ProcessedUrlInfo>
                // where ProcessedUrlInfo contains OriginalUrl, ExpandedUrl, Title.

                var processedUrlResults = await _urlProcessingService.ProcessUrlsInTextAsync(textContent);

                if (processedUrlResults == null || !processedUrlResults.Any())
                {
                    _logger.Information("No URLs found or processed in text from OriginalMessageId: {OriginalMessageId}", streamMessage.OriginalMessageId);
                    return;
                }

                var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                var reportMessages = new List<string>();

                foreach (var result in processedUrlResults)
                {
                    _logger.Information("Original URL: {OriginalUrl}, Processed URL: {ProcessedUrl} from OriginalMessageId: {OriginalMessageId}", 
                        result.OriginalUrl, result.ProcessedUrl ?? "N/A", streamMessage.OriginalMessageId);
                    
                    string messageEntry = $"原始链接: {result.OriginalUrl}";
                    if (!string.IsNullOrEmpty(result.ProcessedUrl) && result.ProcessedUrl != result.OriginalUrl)
                    {
                        messageEntry += $"\n处理后: {result.ProcessedUrl}";
                    }
                    // Title fetching is not currently part of UrlProcessingService.
                    // messageEntry += "\n标题: (获取功能暂未实现)"; 
                    reportMessages.Add(messageEntry);
                }
                
                if (reportMessages.Any())
                {
                    string combinedMessage = "文本中提取到的链接：\n\n" + string.Join("\n\n---\n\n", reportMessages);
                    await senderGrain.SendMessageAsync(new TelegramMessageToSend
                    {
                        ChatId = streamMessage.ChatId,
                        Text = combinedMessage,
                        ReplyToMessageId = (int)streamMessage.OriginalMessageId // Ensure OriginalMessageId is int if API expects int
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during URL extraction for OriginalMessageId: {OriginalMessageId}", streamMessage.OriginalMessageId);
                var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                await senderGrain.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = streamMessage.ChatId,
                    Text = $"处理文本中的链接时出错: {ex.Message}",
                    ReplyToMessageId = (int)streamMessage.OriginalMessageId
                });
            }
        }


        public Task OnCompletedAsync()
        {
            _logger.Information("UrlExtractionGrain {GrainId} completed stream processing.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.Error(ex, "UrlExtractionGrain {GrainId} encountered an error on stream.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.Information("UrlExtractionGrain {GrainId} deactivating. Reason: {Reason}", this.GetGrainId(), reason);
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

    // Placeholder for a more structured URL info object if needed for another stream
    // [GenerateSerializer]
    // public class ProcessedUrlInfo
    // {
    //     [Id(0)]
    //     public string OriginalUrl { get; set; }
    //     [Id(1)]
    //     public string ExpandedUrl { get; set; }
    //     [Id(2)]
    //     public string Title { get; set; }
    //     [Id(3)]
    //     public long OriginalMessageId { get; set; } // To correlate back if needed
    //     [Id(4)]
    //     public long ChatId { get; set; }
    // }
}

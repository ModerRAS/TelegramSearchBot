using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore; // Added for FirstOrDefaultAsync
using TelegramSearchBot.Interfaces;
using TelegramSearchBot.Model;    // For StreamMessage, OrleansStreamConstants, DataDbContext
using TelegramSearchBot.Model.Data; // Added for ShortUrlMapping
using TelegramSearchBot.Service.Common; // For UrlProcessingService, UrlProcessResult

namespace TelegramSearchBot.Grains
{
    public class UrlExtractionGrain : Grain, IUrlExtractionGrain, IAsyncObserver<StreamMessage<string>>
    {
        private readonly UrlProcessingService _urlProcessingService;
        private readonly DataDbContext _dbContext; // For storing ShortUrlMapping
        private readonly ILogger _logger;
        private IAsyncStream<StreamMessage<string>> _textContentStreamSubscription;

        public UrlExtractionGrain(
            UrlProcessingService urlProcessingService,
            DataDbContext dbContext, // Inject DbContext
            ILogger logger) // Logger can be injected directly
        {
            _urlProcessingService = urlProcessingService ?? throw new ArgumentNullException(nameof(urlProcessingService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger?.ForContext<UrlExtractionGrain>() ?? Log.ForContext<UrlExtractionGrain>();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("UrlExtractionGrain {GrainId} activated.", this.GetGrainId());
            var streamProvider = this.GetStreamProvider("DefaultSMSProvider");
            _textContentStreamSubscription = streamProvider.GetStream<StreamMessage<string>>(
                OrleansStreamConstants.TextContentToProcessStreamName,
                OrleansStreamConstants.TextContentStreamNamespace);
            await _textContentStreamSubscription.SubscribeAsync(this);
            await base.OnActivateAsync(cancellationToken);
        }

        // Handles automatic background URL processing from the stream
        public async Task OnNextAsync(StreamMessage<string> streamMessage, StreamSequenceToken token = null)
        {
            var textContent = streamMessage.Payload;
            if (string.IsNullOrWhiteSpace(textContent))
            {
                return;
            }

            _logger.Debug("UrlExtractionGrain {GrainId}: Received text for background URL processing. OriginalMessageId: {OriginalMessageId}", 
                this.GetGrainId(), streamMessage.OriginalMessageId);

            try
            {
                List<UrlProcessResult> results = await _urlProcessingService.ProcessUrlsInTextAsync(textContent);
                if (results.Any())
                {
                    foreach (var result in results)
                    {
                        if (!string.IsNullOrWhiteSpace(result.ProcessedUrl) && result.OriginalUrl != result.ProcessedUrl)
                        {
                            // Check if mapping already exists to avoid duplicates or decide on update strategy
                            var existingMapping = await _dbContext.ShortUrlMappings
                                .FirstOrDefaultAsync(m => m.OriginalUrl == result.OriginalUrl, cancellationToken: CancellationToken.None);

                            if (existingMapping == null)
                            {
                                var newMapping = new ShortUrlMapping
                                {
                                    OriginalUrl = result.OriginalUrl, // Corrected property name
                                    ExpandedUrl = result.ProcessedUrl, // Corrected property name
                                    CreationDate = DateTime.UtcNow    // Corrected property name
                                };
                                _dbContext.ShortUrlMappings.Add(newMapping);
                                _logger.Information("UrlExtractionGrain {GrainId}: Storing new URL mapping: {OriginalUrl} -> {ProcessedUrl}",
                                    this.GetGrainId(), result.OriginalUrl, result.ProcessedUrl);
                            }
                            else if (existingMapping.ExpandedUrl != result.ProcessedUrl) // Corrected property name
                            {
                                // Optionally update if the long URL changed, or log
                                _logger.Information("UrlExtractionGrain {GrainId}: Existing mapping for {OriginalUrl} found. Current: {ExistingLongUrl}, New: {ProcessedUrl}. Not updating for now.",
                                    this.GetGrainId(), result.OriginalUrl, existingMapping.ExpandedUrl, result.ProcessedUrl); // Corrected property name
                            }
                        }
                    }
                    await _dbContext.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "UrlExtractionGrain {GrainId}: Error during background URL processing for OriginalMessageId: {OriginalMessageId}",
                    this.GetGrainId(), streamMessage.OriginalMessageId);
            }
        }

        // Handles explicit /resolveurls command
        public async Task<string> GetFormattedResolvedUrlsAsync(string textToParse)
        {
            _logger.Information("UrlExtractionGrain {GrainId}: GetFormattedResolvedUrlsAsync called with text: \"{TextToParse}\"", 
                this.GetGrainId(), textToParse);

            if (string.IsNullOrWhiteSpace(textToParse))
            {
                return "请提供包含URL的文本。";
            }

            try
            {
                List<UrlProcessResult> results = await _urlProcessingService.ProcessUrlsInTextAsync(textToParse);
                if (!results.Any())
                {
                    return "未在提供的文本中找到URL。";
                }

                var sb = new StringBuilder("URL 解析结果:\n");
                foreach (var result in results)
                {
                    if (!string.IsNullOrWhiteSpace(result.ProcessedUrl) && result.OriginalUrl != result.ProcessedUrl)
                    {
                        sb.AppendLine($"- 原始: {result.OriginalUrl}\n  最终: {result.ProcessedUrl}");
                    }
                    else if (!string.IsNullOrWhiteSpace(result.ProcessedUrl) && result.OriginalUrl == result.ProcessedUrl)
                    {
                        sb.AppendLine($"- 链接: {result.OriginalUrl} (无变化或已是最终链接，已清理追踪参数)");
                    }
                    else
                    {
                        sb.AppendLine($"- 原始: {result.OriginalUrl} (解析失败或无法访问)");
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "UrlExtractionGrain {GrainId}: Error in GetFormattedResolvedUrlsAsync for text: \"{TextToParse}\"", 
                    this.GetGrainId(), textToParse);
                return "解析URL时发生内部错误。";
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
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Common;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Controller.Storage;
using TelegramSearchBot.Controller.AI.QR;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Common.Model;

namespace TelegramSearchBot.Controller.Common {
    public class UrlProcessingController : IOnUpdate
    {
        private readonly IShortUrlMappingService _shortUrlMappingService;
        private readonly UrlProcessingService _urlProcessingService;
        private readonly ILogger<UrlProcessingController> _logger;

        public UrlProcessingController(
            IShortUrlMappingService shortUrlMappingService,
            UrlProcessingService urlProcessingService,
            ILogger<UrlProcessingController> logger)
        {
            _shortUrlMappingService = shortUrlMappingService;
            _urlProcessingService = urlProcessingService;
            _logger = logger;
        }

        public List<Type> Dependencies => new List<Type>() { typeof(MessageController), typeof(AutoQRController) };

        public async Task ExecuteAsync(PipelineContext p)
        {
            // 处理所有ProcessingResults中的URL
            var processingResults = new List<UrlProcessResult>();
            
            foreach (var result in p.ProcessingResults)
            {
                var urls = await _urlProcessingService.ProcessUrlsInTextAsync(result);
                if (urls != null)
                {
                    processingResults.AddRange(urls);
                }
            }

            if (!processingResults.Any())
            {
                return;
            }

            // 保存处理后的URL映射
            var mappingsToSave = processingResults
                .Where(r => !string.IsNullOrWhiteSpace(r.ProcessedUrl) && r.OriginalUrl != r.ProcessedUrl)
                .Select(r => new ShortUrlMapping
                {
                    OriginalUrl = r.OriginalUrl,
                    ExpandedUrl = r.ProcessedUrl,
                    CreationDate = DateTime.UtcNow
                })
                .ToList();

            if (mappingsToSave.Any())
            {
                await _shortUrlMappingService.SaveUrlMappingsAsync(mappingsToSave, CancellationToken.None);
            }
        }
    }
}
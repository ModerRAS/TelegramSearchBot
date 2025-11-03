using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Core.Model.Notifications;
using TelegramSearchBot.Service.Common;

namespace TelegramSearchBot.Handler {
    public class ProcessUrlHandler : IRequestHandler<ProcessUrlRequest, string> {
        private readonly UrlProcessingService _urlProcessingService;

        public ProcessUrlHandler(UrlProcessingService urlProcessingService) {
            _urlProcessingService = urlProcessingService;
        }

        public async Task<string> Handle(ProcessUrlRequest request, CancellationToken cancellationToken) {
            try {
                var result = await _urlProcessingService.ProcessUrlAsync(request.Url);
                if (!result.Contains("b23.tv/")) {
                    return result;
                }
                return request.Url;
            } catch (Exception ex) {
                request.Logger?.LogError(ex, "Error resolving URL: {Url}", request.Url);
                return request.Url;
            }
        }
    }
}

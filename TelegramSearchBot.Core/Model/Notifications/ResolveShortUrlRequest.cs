using MediatR;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Core.Model.Notifications {
    public class ProcessUrlRequest : IRequest<string> {
        public string Url { get; }
        public ILogger Logger { get; }

        public ProcessUrlRequest(string url, ILogger logger) {
            Url = url;
            Logger = logger;
        }
    }
}

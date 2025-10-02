using System.Threading;
using System.Threading.Tasks;
using MediatR;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Search.FAISS.Service;

namespace TelegramSearchBot.Handler {
    public class MessageVectorGenerationHandler : INotificationHandler<MessageVectorGenerationNotification> {
        private readonly FaissVectorService _faissVectorService;

        public MessageVectorGenerationHandler(FaissVectorService faissVectorService) {
            _faissVectorService = faissVectorService;
        }

        public async Task Handle(MessageVectorGenerationNotification notification, CancellationToken cancellationToken) {
            await _faissVectorService.StoreMessageAsync(notification.Message);
        }
    }
}

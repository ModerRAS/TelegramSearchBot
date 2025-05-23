using MediatR;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Service.Vector;

namespace TelegramSearchBot.Handler
{
    public class MessageVectorGenerationHandler : INotificationHandler<MessageVectorGenerationNotification>
    {
        private readonly VectorGenerationService _vectorGenerationService;

        public MessageVectorGenerationHandler(VectorGenerationService vectorGenerationService)
        {
            _vectorGenerationService = vectorGenerationService;
        }

        public async Task Handle(MessageVectorGenerationNotification notification, CancellationToken cancellationToken)
        {
            await _vectorGenerationService.StoreMessageAsync(notification.Message);
        }
    }
}
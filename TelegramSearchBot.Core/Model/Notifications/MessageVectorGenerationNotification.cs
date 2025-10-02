using MediatR;
using TelegramSearchBot.Core.Model.Data;

namespace TelegramSearchBot.Core.Model.Notifications {
    public class MessageVectorGenerationNotification : INotification {
        public Message Message { get; }

        public MessageVectorGenerationNotification(Message message) {
            Message = message;
        }
    }
}

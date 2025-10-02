using MediatR;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Model.Notifications {
    public class MessageVectorGenerationNotification : INotification {
        public Message Message { get; }

        public MessageVectorGenerationNotification(Message message) {
            Message = message;
        }
    }
}

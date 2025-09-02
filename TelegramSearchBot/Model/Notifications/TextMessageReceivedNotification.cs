using MediatR;
using Telegram.Bot.Types; // Added for Message
using Telegram.Bot.Types.Enums;

namespace TelegramSearchBot.Model.Notifications {
    public class TextMessageReceivedNotification : INotification {
        public string Text { get; } // Kept for convenience if only text is needed
        public long ChatId { get; } // Kept for convenience
        public int MessageId { get; }  // Kept for convenience
        public ChatType ChatType { get; } // Kept for convenience
        public Message OriginalMessage { get; } // The full original message

        public TextMessageReceivedNotification(Message originalMessage) {
            OriginalMessage = originalMessage;
            Text = originalMessage.Text ?? string.Empty;
            ChatId = originalMessage.Chat.Id;
            MessageId = originalMessage.MessageId;
            ChatType = originalMessage.Chat.Type;
        }

        // Overloaded constructor for cases where we only have text and metadata, not a full Message object
        // This might be used by internal services that generate text to be processed like a user message.
        public TextMessageReceivedNotification(string text, long chatId, int messageId, ChatType chatType, Message? originalMessage = null) {
            Text = text;
            ChatId = chatId;
            MessageId = messageId; // This would be the ID of the message that triggered this, e.g., the photo message
            ChatType = chatType;
            OriginalMessage = originalMessage; // Will be null in this case if not provided
        }
    }
}

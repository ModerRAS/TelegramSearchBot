using MediatR;
using Telegram.Bot.Types.Enums; // Added for ChatType

namespace TelegramSearchBot.Model.Notifications
{
    public class TextMessageReceivedNotification : INotification
    {
        public string Text { get; }
        public long ChatId { get; }
        public int MessageId { get; } 
        public ChatType ChatType { get; } 

        public TextMessageReceivedNotification(string text, long chatId, int messageId, ChatType chatType)
        {
            Text = text;
            ChatId = chatId;
            MessageId = messageId;
            ChatType = chatType;
        }
    }
}

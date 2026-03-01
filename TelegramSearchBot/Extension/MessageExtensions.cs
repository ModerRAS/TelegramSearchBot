using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Extension {
    public static class MessageExtensions {
        public static Message FromTelegramMessage(Telegram.Bot.Types.Message telegramMessage) {
            return new Message {
                MessageId = telegramMessage.MessageId,
                GroupId = telegramMessage.Chat.Id,
                FromUserId = telegramMessage.From?.Id ?? 0,
                ReplyToUserId = telegramMessage.ReplyToMessage?.From?.Id ?? 0,
                ReplyToMessageId = telegramMessage.ReplyToMessage?.MessageId ?? 0,
                Content = telegramMessage.Text ?? telegramMessage.Caption ?? string.Empty,
                DateTime = telegramMessage.Date
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramSearchBot.Model.Data {
    public class Message {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public DateTime DateTime { get; set; }
        public long GroupId { get; set; }
        public long MessageId { get; set; }
        public long FromUserId { get; set; }
        public long ReplyToUserId { get; set; }
        public long ReplyToMessageId { get; set; }
        public string Content { get; set; }

        public virtual ICollection<MessageExtension> MessageExtensions { get; set; }

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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lucene.Net.Documents;
using TelegramSearchBot.Search.Model;

namespace TelegramSearchBot.Search.Tool {
    internal static class DocumentMessageMapper {
        public static MessageDTO? Map(Document document) {
            if (document == null) {
                return null;
            }

            if (!TryGetInt64(document, "Id", out var id)) {
                return null;
            }

            var message = new MessageDTO {
                Id = id,
                GroupId = TryGetInt64(document, "GroupId", out var groupId) ? groupId : 0,
                MessageId = TryGetInt64(document, "MessageId", out var messageId) ? messageId : 0,
                FromUserId = TryGetInt64(document, "FromUserId", out var fromUserId) ? fromUserId : 0,
                ReplyToUserId = TryGetInt64(document, "ReplyToUserId", out var replyToUserId) ? replyToUserId : 0,
                ReplyToMessageId = TryGetInt64(document, "ReplyToMessageId", out var replyToMessageId) ? replyToMessageId : 0,
                Content = document.Get("Content") ?? string.Empty,
                MessageExtensions = new List<MessageExtensionDTO>()
            };

            var dateTimeRaw = document.Get("DateTime");
            if (DateTime.TryParse(dateTimeRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate)) {
                message.DateTime = parsedDate;
            }

            foreach (var field in document.Fields.Where(f => f.Name != null && f.Name.StartsWith("Ext_", StringComparison.Ordinal))) {
                var fieldName = field.Name.Substring(4);
                var fieldValue = document.Get(field.Name);
                if (string.IsNullOrEmpty(fieldValue)) {
                    continue;
                }

                message.MessageExtensions.Add(new MessageExtensionDTO {
                    Name = fieldName,
                    Value = fieldValue
                });
            }

            return message;
        }

        private static bool TryGetInt64(Document document, string fieldName, out long value) {
            var raw = document.Get(fieldName);
            return long.TryParse(raw, out value);
        }
    }
}

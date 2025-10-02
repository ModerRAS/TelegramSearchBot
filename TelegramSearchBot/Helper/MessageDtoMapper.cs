using System;
using System.Collections.Generic;
using System.Linq;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Search.Lucene.Model;

namespace TelegramSearchBot.Helper {
    public static class MessageDtoMapper {
        public static MessageDTO ToDto(Message message) {
            if (message == null) {
                throw new ArgumentNullException(nameof(message));
            }

            var dto = new MessageDTO {
                Id = message.Id,
                DateTime = message.DateTime,
                GroupId = message.GroupId,
                MessageId = message.MessageId,
                FromUserId = message.FromUserId,
                ReplyToUserId = message.ReplyToUserId,
                ReplyToMessageId = message.ReplyToMessageId,
                Content = message.Content ?? string.Empty,
                MessageExtensions = new List<MessageExtensionDTO>()
            };

            if (message.MessageExtensions != null) {
                dto.MessageExtensions.AddRange(message.MessageExtensions.Select(ext => new MessageExtensionDTO {
                    Name = ext.Name ?? string.Empty,
                    Value = ext.Value ?? string.Empty
                }));
            }

            return dto;
        }

        public static List<MessageDTO> ToDtoList(IEnumerable<Message> messages) {
            if (messages == null) {
                throw new ArgumentNullException(nameof(messages));
            }

            return messages.Select(ToDto).ToList();
        }

        public static Message ToEntity(MessageDTO dto) {
            if (dto == null) {
                throw new ArgumentNullException(nameof(dto));
            }

            var message = new Message {
                Id = dto.Id,
                DateTime = dto.DateTime,
                GroupId = dto.GroupId,
                MessageId = dto.MessageId,
                FromUserId = dto.FromUserId,
                ReplyToUserId = dto.ReplyToUserId,
                ReplyToMessageId = dto.ReplyToMessageId,
                Content = dto.Content
            };

            var extensions = dto.MessageExtensions?.Select(ext => new MessageExtension {
                Name = ext.Name,
                Value = ext.Value
            }).ToList() ?? new List<MessageExtension>();

            message.MessageExtensions = extensions;
            return message;
        }

        public static List<Message> ToEntityList(IEnumerable<MessageDTO> dtos) {
            if (dtos == null) {
                throw new ArgumentNullException(nameof(dtos));
            }

            return dtos.Select(ToEntity).ToList();
        }
    }
}

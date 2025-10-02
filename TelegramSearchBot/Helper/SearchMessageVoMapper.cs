using System;
using System.Collections.Generic;
using System.Linq;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Search;
using TelegramSearchBot.Search.Lucene.Model;

namespace TelegramSearchBot.Helper {
    public static class SearchMessageVoMapper {
        public static SearchMessageVO FromSearchOption(SearchOption searchOption) {
            if (searchOption == null) {
                throw new ArgumentNullException(nameof(searchOption));
            }

            return new SearchMessageVO {
                ChatId = searchOption.ChatId,
                Count = searchOption.Count,
                Skip = searchOption.Skip,
                Take = searchOption.Take,
                SearchType = searchOption.SearchType,
                Messages = ToMessageVoList(searchOption.Messages, searchOption.Search)
            };
        }

        public static List<MessageVO> ToMessageVoList(IEnumerable<Message> messages, string keyword) {
            if (messages == null) {
                return new List<MessageVO>();
            }

            return messages
                .Where(message => message != null)
                .Select(message => new MessageVO(message, keyword))
                .ToList();
        }

        public static List<MessageVO> ToMessageVoList(IEnumerable<MessageDTO> messageDtos, string keyword) {
            if (messageDtos == null) {
                return new List<MessageVO>();
            }

            var entities = messageDtos
                .Where(dto => dto != null)
                .Select(dto => MessageDtoMapper.ToEntity(dto))
                .ToList();
            return ToMessageVoList(entities, keyword);
        }
    }
}

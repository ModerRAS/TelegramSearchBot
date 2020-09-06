using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Model;
using System.Linq;

namespace TelegramSearchBot.Service {
    class SearchService {
        private readonly SearchContext DbContext;
        public SearchService(SearchContext DbContext) {
            this.DbContext = DbContext;
        }

        public (SearchOption, List<Message>) Search(SearchOption searchOption) {
            var query = from s in DbContext.Messages
                        where s.Content.Contains(searchOption.Search) && (searchOption.IsGroup ? s.GroupId.Equals(searchOption.ChatId) : (from u in DbContext.Users where u.UserId.Equals(searchOption.ChatId) select u.GroupId).Contains(s.GroupId))
                        orderby s.MessageId descending
                        select s;
            if (searchOption.Count < 0) {
                searchOption.Count = query.Count();
            }
            var Finded = query.Skip(searchOption.Skip).Take(searchOption.Take).ToList();
            return (searchOption, Finded);
        }
    }
}

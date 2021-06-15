using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Model;
using System.Linq;
using TelegramSearchBot.Intrerface;
using System.Threading.Tasks;

namespace TelegramSearchBot.Service {
    public class SearchService : ISearchService, IService {
        private readonly SearchContext DbContext;
        public SearchService(SearchContext DbContext) {
            this.DbContext = DbContext;
        }

        public string ServiceName => "SearchService";

        public async Task<SearchOption> Search(SearchOption searchOption) {
            var query = from s in DbContext.Messages
                        where s.Content.Contains(searchOption.Search) && (searchOption.IsGroup ? s.GroupId.Equals(searchOption.ChatId) : (from u in DbContext.Users where u.UserId.Equals(searchOption.ChatId) select u.GroupId).Contains(s.GroupId))
                        orderby s.MessageId descending
                        select s;
            if (searchOption.Count < 0) {
                searchOption.Count = query.Count();
            }
            searchOption.Messages = query.Skip(searchOption.Skip).Take(searchOption.Take).ToList();
            return searchOption;
        }
    }
}

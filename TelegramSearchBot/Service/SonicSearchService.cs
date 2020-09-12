using System;
using System.Collections.Generic;
using System.Text;
using TelegramSearchBot.Model;
using System.Linq;
using TelegramSearchBot.Intrerface;
using NSonic;
using System.Threading.Tasks;
using Telegram.Bot.Exceptions;

namespace TelegramSearchBot.Service {
    class SonicSearchService : ISearchService {
        private readonly SearchContext DbContext;
        public SonicSearchService(SearchContext DbContext) {
            this.DbContext = DbContext;
        }

        /// <summary>
        /// 私聊搜索返回值： 群组Id:消息Id
        /// 群聊搜索返回值： 群组Id:消息Id
        /// </summary>
        /// <param name="searchOption"></param>
        /// <returns></returns>
        public override async Task<SearchOption> Search(SearchOption searchOption) {

            using (var sonicSearchConnection = NSonicFactory.Search(Env.SonicHostname, Env.SonicPort, Env.SonicSecret)) {
                await sonicSearchConnection.ConnectAsync();

                var sonicQuery = await sonicSearchConnection.QueryAsync(Env.SonicCollection, searchOption.ChatId.ToString(), searchOption.Search, limit: searchOption.Take, offset: searchOption.Skip);

                if (sonicQuery.Length == searchOption.Take) {
                    var sonicQueryCount = await sonicSearchConnection.QueryAsync(Env.SonicCollection, searchOption.ChatId.ToString(), searchOption.Search, limit: searchOption.Take, offset: searchOption.Skip + searchOption.Take);
                    searchOption.Count = sonicQueryCount.Length + sonicQuery.Length + searchOption.Skip;
                } else {
                    searchOption.Count = sonicQuery.Length + searchOption.Skip;
                }

                var MessagesIds = new Dictionary<long, long>();

                foreach (var e in sonicQuery) {
                    var tmp = e.Split(":");
                    if (tmp.Length == 2) MessagesIds.Add(long.Parse(tmp[0]), long.Parse(tmp[1]));
                }

                searchOption.Messages = new List<Message>();

                foreach (var e in MessagesIds) {
                    var query = from s in DbContext.Messages
                                where e.Key.Equals(s.GroupId) && e.Value.Equals(s.MessageId)
                                select s;
                    searchOption.Messages.Add(query.FirstOrDefault());
                }
                return searchOption;
            }
        }
    }
}

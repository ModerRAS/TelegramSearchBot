using System;
using System.Collections.Generic;
using System.Text;
using TelegramSearchBot.Model;
using System.Linq;
using TelegramSearchBot.Intrerface;
using NSonic;
using System.Threading.Tasks;
using Telegram.Bot.Exceptions;
using Microsoft.Extensions.Caching.Distributed;

namespace TelegramSearchBot.Service {
    public class SonicSearchService : ISearchService {
        //private readonly SearchContext DbContext;
        private readonly IDistributedCache Cache;
        public SonicSearchService(
            //SearchContext DbContext, 
            IDistributedCache Cache) {
            //this.DbContext = DbContext;
            this.Cache = Cache;
        }

        /// <summary>
        /// 私聊搜索返回值： 群组Id:消息Id
        /// 群聊搜索返回值： 群组Id:消息Id
        /// </summary>
        /// <param name="searchOption"></param>
        /// <returns></returns>
        public async Task<SearchOption> Search(SearchOption searchOption) {

            using (var sonicSearchConnection = NSonicFactory.Search(Env.SonicHostname, Env.SonicPort, Env.SonicSecret)) {
                await sonicSearchConnection.ConnectAsync();

                var sonicQuery = await sonicSearchConnection.QueryAsync(searchOption.ChatId.ToString(), Env.SonicCollection, searchOption.Search, limit: searchOption.Take, offset: searchOption.Skip);

                if (sonicQuery.Length == searchOption.Take) {
                    var sonicQueryCount = await sonicSearchConnection.QueryAsync(Env.SonicCollection, searchOption.ChatId.ToString(), searchOption.Search, limit: searchOption.Take, offset: searchOption.Skip + searchOption.Take);
                    searchOption.Count = sonicQueryCount.Length + sonicQuery.Length + searchOption.Skip;
                } else {
                    searchOption.Count = sonicQuery.Length + searchOption.Skip;
                }

                var Messages = new HashSet<Message>();

                foreach (var e in sonicQuery) {
                    var tmp = e.Split(":");
                    long groupid, messageid;
                    if (long.TryParse(tmp[0], out groupid) && long.TryParse(tmp[1], out messageid)) {
                        Messages.Add(new Message() {
                            Id = 0,
                            GroupId = groupid,
                            MessageId = messageid,
                            Content = Encoding.UTF8.GetString(await Cache.GetAsync($"{groupid}:{messageid}"))
                        });
                    }
                    
                }
                searchOption.Messages = new List<Message>(Messages);
                return searchOption;
            }
        }
    }
}

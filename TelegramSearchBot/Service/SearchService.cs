using System.Linq;
using TelegramSearchBot.Intrerface;
using System.Threading.Tasks;
using TelegramSearchBot.Manager;
using System.Collections.Generic;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service {
    public class SearchService : ISearchService, IService {
        private readonly LuceneManager lucene;
        public SearchService(LuceneManager lucene) {
            this.lucene = lucene;
        }

        public string ServiceName => "SearchService";

#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public async Task<SearchOption> Search(SearchOption searchOption) {
            if (searchOption.IsGroup) {
                (searchOption.Count, searchOption.Messages) = lucene.Search(searchOption.Search, searchOption.ChatId, searchOption.Skip, searchOption.Take);
            } else {
                var Users = Env.Database.GetCollection<User>("Users");
                var UserInGroups =  Users.Find(user => searchOption.ChatId.Equals(user.UserId)).ToList();
                var GroupsLength = UserInGroups.Count;
                searchOption.Messages = new List<Message>();
                foreach (var Group in UserInGroups) {
                    var (count, messages) = lucene.Search(searchOption.Search, Group.GroupId, searchOption.Skip / GroupsLength, searchOption.Take / GroupsLength);
                    searchOption.Messages.AddRange(messages);
                    searchOption.Count += count;
                }
            }
            return searchOption;
        }
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
    }
}

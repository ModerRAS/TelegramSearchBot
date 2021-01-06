using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Intrerface {
    public interface ISearchService {
        public abstract Task<SearchOption> Search(SearchOption searchOption);
    }
}

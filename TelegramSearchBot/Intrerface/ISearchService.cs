using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.CommonModel;

namespace TelegramSearchBot.Intrerface {
    public interface ISearchService{
        public abstract Task<SearchOption> Search(SearchOption searchOption);
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Common.DTO;

namespace TelegramSearchBot.Common.Intrerface
{
    public interface ISearchService{
        public abstract Task<SearchOption> Search(SearchOption searchOption);
    }
}

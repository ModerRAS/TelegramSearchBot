using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Interface {
    public interface ISearchService {
        public abstract Task<SearchOption> Search(SearchOption searchOption);
    }
}

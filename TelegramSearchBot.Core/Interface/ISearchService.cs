using System.Threading.Tasks;
using ModelSearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.Interface {
    public interface ISearchService {
        public abstract Task<ModelSearchOption> Search(ModelSearchOption searchOption);
    }
}

using System.Threading.Tasks;
using ModelSearchOption = TelegramSearchBot.Core.Model.SearchOption;

namespace TelegramSearchBot.Core.Interface {
    public interface ISearchService {
        public abstract Task<ModelSearchOption> Search(ModelSearchOption searchOption);
    }
}

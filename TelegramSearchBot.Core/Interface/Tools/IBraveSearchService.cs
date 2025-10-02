using System.Threading.Tasks;
using TelegramSearchBot.Core.Model.Tools;

namespace TelegramSearchBot.Core.Interface.Tools {
    public interface IBraveSearchService {
        Task<BraveSearchResult> SearchWeb(string query, int page = 1, int count = 5, string country = "us", string searchLang = "en");
    }
}

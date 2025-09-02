using System.Threading.Tasks;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools {
    public interface IBraveSearchService {
        Task<BraveSearchResult> SearchWeb(string query, int page = 1, int count = 5, string country = "us", string searchLang = "en");
    }
}

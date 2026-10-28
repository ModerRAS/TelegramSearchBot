using System.Threading.Tasks;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools {
    public interface IDeepSeekSearchService {
        Task<DeepSeekSearchResult> SearchWebByDeepSeek(string query, int count = 5, string oriQuestion = "", string localization = "", bool needContent = false);
    }
}

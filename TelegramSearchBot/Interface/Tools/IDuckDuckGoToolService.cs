using System.Threading.Tasks;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools
{
    public interface IDuckDuckGoToolService
    {
        Task<DuckDuckGoSearchResult> SearchWeb(string query, int page = 1);
        DuckDuckGoSearchResult ParseHtml(string html, string query, int page = 1);
    }
} 
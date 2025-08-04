using System.Threading.Tasks;

namespace TelegramSearchBot.Interface.Tools
{
    public interface IPuppeteerArticleExtractorService
    {
        Task<string> ExtractArticleContent(string url);
    }
} 
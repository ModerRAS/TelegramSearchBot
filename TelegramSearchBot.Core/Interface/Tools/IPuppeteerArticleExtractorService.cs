using System.Threading.Tasks;

namespace TelegramSearchBot.Core.Interface.Tools {
    public interface IPuppeteerArticleExtractorService {
        Task<string> ExtractArticleContent(string url);
    }
}

using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Interface.Tools {
    public interface IFileToolService {
        Task<string> ReadFile(string path, ToolContext toolContext, int? startLine = null, int? endLine = null);
        Task<string> WriteFile(string path, string content, ToolContext toolContext);
        Task<string> EditFile(string path, string oldText, string newText, ToolContext toolContext);
        Task<string> SearchText(string pattern, ToolContext toolContext, string path = null, string fileGlob = null, bool ignoreCase = true);
        Task<string> ListFiles(ToolContext toolContext, string path = null, string pattern = null);
    }
}

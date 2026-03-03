using System.Threading.Tasks;

namespace TelegramSearchBot.Interface.Tools {
    public interface IFileToolService {
        Task<string> ReadFile(string path, int? startLine = null, int? endLine = null);
        Task<string> WriteFile(string path, string content);
        Task<string> EditFile(string path, string oldText, string newText);
        Task<string> SearchText(string pattern, string path = null, string fileGlob = null, bool ignoreCase = true);
        Task<string> ListFiles(string path = null, string pattern = null);
    }
}

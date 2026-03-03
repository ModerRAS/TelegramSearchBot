using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Interface.Tools {
    public interface IBashToolService {
        Task<string> ExecuteCommand(string command, ToolContext toolContext, string workingDirectory = null, int timeoutMs = 30000);
    }
}

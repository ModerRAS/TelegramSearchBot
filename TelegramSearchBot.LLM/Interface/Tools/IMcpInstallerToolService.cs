using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Mcp;

namespace TelegramSearchBot.Interface.Tools {
    public interface IMcpInstallerToolService {
        Task<string> ListMcpServers();
        Task<string> AddMcpServer(string name, string command, string args, ToolContext toolContext, string env = null);
        Task<string> RemoveMcpServer(string name, ToolContext toolContext);
        Task<string> RestartMcpServers(ToolContext toolContext);
    }
}

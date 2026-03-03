using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Mcp;

namespace TelegramSearchBot.Interface.Tools {
    public interface IMcpInstallerToolService {
        Task<string> ListMcpServers();
        Task<string> AddMcpServer(string name, string command, string args, string env);
        Task<string> RemoveMcpServer(string name);
        Task<string> RestartMcpServers();
    }
}

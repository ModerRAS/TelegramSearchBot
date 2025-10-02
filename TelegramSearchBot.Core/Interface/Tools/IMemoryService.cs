using System.Threading.Tasks;
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Core.Model.Tools;

namespace TelegramSearchBot.Core.Interface.Tools {
    public interface IMemoryService {
        Task<object> ProcessMemoryCommandAsync(string command, string arguments, ToolContext toolContext);
    }
}

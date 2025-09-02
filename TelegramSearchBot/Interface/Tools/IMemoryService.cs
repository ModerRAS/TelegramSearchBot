using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools {
    public interface IMemoryService {
        Task<object> ProcessMemoryCommandAsync(string command, string arguments, ToolContext toolContext);
    }
}

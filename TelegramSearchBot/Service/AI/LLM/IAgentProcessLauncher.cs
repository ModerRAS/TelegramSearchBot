using System.Threading;
using System.Threading.Tasks;

namespace TelegramSearchBot.Service.AI.LLM {
    public interface IAgentProcessLauncher {
        Task<int> StartAsync(long chatId, CancellationToken cancellationToken = default);
        bool TryKill(int processId);
    }
}

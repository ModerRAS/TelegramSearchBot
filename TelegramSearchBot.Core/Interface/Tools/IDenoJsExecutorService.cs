using System.Threading.Tasks;

namespace TelegramSearchBot.Core.Interface.Tools {
    public interface IDenoJsExecutorService {
        Task<string> ExecuteJs(string jsCode, int timeoutMs = 5000);
    }
}

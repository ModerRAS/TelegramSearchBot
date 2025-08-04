using System.Threading.Tasks;

namespace TelegramSearchBot.Interface.Tools
{
    public interface IDenoJsExecutorService
    {
        Task<string> ExecuteJs(string jsCode, int timeoutMs = 5000);
    }
} 
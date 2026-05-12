using System.Threading;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interface.AI.LLM {
    public interface IGroupLlmSettingsService {
        Task<string> GetModelAsync(long chatId, CancellationToken cancellationToken = default);
        Task<(string Previous, string Current)> SetModelAsync(long chatId, string modelName, CancellationToken cancellationToken = default);
    }
}

using System.Threading;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interface.AI.LLM {
    public interface IBotIdentityProvider {
        Task<BotIdentity> GetIdentityAsync(CancellationToken cancellationToken = default);
        void SetIdentity(long botUserId, string botName);
    }

    public sealed record BotIdentity(long UserId, string UserName);
}

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Singleton)]
    public class BotIdentityProvider : IService, IBotIdentityProvider {
        private readonly object _lock = new();
        private BotIdentity _identity = new(0, string.Empty);

        public string ServiceName => nameof(BotIdentityProvider);

        public Task<BotIdentity> GetIdentityAsync(CancellationToken cancellationToken = default) {
            lock (_lock) {
                return Task.FromResult(_identity);
            }
        }

        public void SetIdentity(long botUserId, string botName) {
            lock (_lock) {
                _identity = new BotIdentity(botUserId, botName ?? string.Empty);
                Env.BotId = botUserId;
            }
        }
    }
}

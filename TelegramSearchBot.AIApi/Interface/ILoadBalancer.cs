using TelegramSearchBot.AIApi.Model.BackendServer;

namespace TelegramSearchBot.AIApi.Interface
{
    public interface ILoadBalancer {
        BackendServerSettings GetNextBackend();
    }
}

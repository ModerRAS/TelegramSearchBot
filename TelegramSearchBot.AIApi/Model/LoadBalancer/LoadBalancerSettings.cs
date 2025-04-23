using TelegramSearchBot.AIApi.Model.BackendServer;

namespace TelegramSearchBot.AIApi.Model.LoadBalancer {
    public class LoadBalancerSettings {
        public List<BackendServerSettings> Servers { get; set; }
    }
}

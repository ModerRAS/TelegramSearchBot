using Microsoft.Extensions.Options;
using TelegramSearchBot.AIApi.Interface;
using TelegramSearchBot.AIApi.Model.BackendServer;
using TelegramSearchBot.AIApi.Model.LoadBalancer;

namespace TelegramSearchBot.AIApi.LoadBalancer
{
    public class RoundRobinLoadBalancer : ILoadBalancer {
        private readonly List<BackendServerSettings> _servers;
        private int _currentIndex = -1; // 使用 -1 使得第一次调用时索引变为 0

        public RoundRobinLoadBalancer(IOptions<LoadBalancerSettings> settings) {
            // 过滤掉无效的服务器配置，例如没有 URL 的
            _servers = settings.Value.Servers.Where(s => !string.IsNullOrEmpty(s.Url)).ToList();

            if (_servers.Count == 0) {
                throw new InvalidOperationException("No backend servers configured for load balancing.");
            }
        }

        public BackendServerSettings GetNextBackend() {
            // 使用 Interlocked.Increment 来保证线程安全地更新索引
            int nextIndex = Interlocked.Increment(ref _currentIndex);
            return _servers[nextIndex % _servers.Count];
        }
    }
}

using StackExchange.Redis;
using System;
using System.Threading.Tasks;
using TelegramSearchBot;

namespace TelegramSearchBot.Helper {
    /// <summary>
    /// HTTP反向代理Redis配置工具
    /// </summary>
    public static class HttpProxyHelper {
        private const string Prefix = "TelegramSearchBot:HttpProxy:";

        /// <summary>
        /// 添加或更新HTTP反向代理配置
        /// </summary>
        public static void AddProxyConfig(IDatabase redis, int listenPort, string targetUrl) {
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out _)) {
                throw new ArgumentException("Invalid target URL format");
            }

            redis.SetAdd($"{Prefix}Ports", listenPort);
            redis.StringSet($"{Prefix}Config:{listenPort}", targetUrl);
            redis.Publish($"{Prefix}ConfigChanged", "");
        }

        /// <summary>
        /// 异步添加或更新HTTP反向代理配置
        /// </summary>
        public static async Task AddProxyConfigAsync(IDatabaseAsync redis, int listenPort, string targetUrl) {
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out _)) {
                throw new ArgumentException("Invalid target URL format");
            }

            await redis.SetAddAsync($"{Prefix}Ports", listenPort);
            await redis.StringSetAsync($"{Prefix}Config:{listenPort}", targetUrl);
            await redis.PublishAsync($"{Prefix}ConfigChanged", "");
        }

        /// <summary>
        /// 添加代理配置并自动分配随机端口
        /// </summary>
        public static int AddProxyConfigWithRandomPort(IDatabase redis, string targetUrl) {
            // 先检查是否已存在该URL的配置
            int existingPort = GetProxyConfigByUrl(redis, targetUrl);
            if (existingPort != -1) {
                return existingPort;
            }

            int port = Utils.GetRandomAvailablePort();
            if (port == -1) {
                throw new Exception("Failed to get available port");
            }
            AddProxyConfig(redis, port, targetUrl);
            return port;
        }

        /// <summary>
        /// 异步添加代理配置并自动分配随机端口
        /// </summary>
        public static async Task<int> AddProxyConfigWithRandomPortAsync(IDatabaseAsync redis, string targetUrl) {
            // 先检查是否已存在该URL的配置
            int existingPort = await GetProxyConfigByUrlAsync(redis, targetUrl);
            if (existingPort != -1) {
                return existingPort;
            }

            int port = Utils.GetRandomAvailablePort();
            if (port == -1) {
                throw new Exception("Failed to get available port");
            }
            await AddProxyConfigAsync(redis, port, targetUrl);
            return port;
        }

        /// <summary>
        /// 添加代理配置并自动分配随机端口(保留路径)
        /// </summary>
        public static async Task<(int Port, string LocalUrl)> AddProxyConfigWithRandomPortAndPathAsync(IDatabaseAsync redis, string targetUrl) {
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri)) {
                throw new ArgumentException("Invalid target URL format");
            }

            // 提取基础URL(不带路径)
            string baseUrl = $"{uri.Scheme}://{uri.Authority}";
            
            // 检查是否已有该基础URL的配置
            int port = await AddProxyConfigWithRandomPortAsync(redis, baseUrl);
            
            // 构建带路径的本地URL
            string localUrl = $"http://localhost:{port}{uri.PathAndQuery}";

            return (port, localUrl);
        }

        /// <summary>
        /// 移除HTTP反向代理配置
        /// </summary>
        public static void RemoveProxyConfig(IDatabase redis, int listenPort) {
            redis.SetRemove($"{Prefix}Ports", listenPort);
            redis.KeyDelete($"{Prefix}Config:{listenPort}");
            redis.Publish($"{Prefix}ConfigChanged", "");
        }

        /// <summary>
        /// 异步移除HTTP反向代理配置
        /// </summary>
        public static async Task RemoveProxyConfigAsync(IDatabaseAsync redis, int listenPort) {
            await redis.SetRemoveAsync($"{Prefix}Ports", listenPort);
            await redis.KeyDeleteAsync($"{Prefix}Config:{listenPort}");
            await redis.PublishAsync($"{Prefix}ConfigChanged", "");
        }

        /// <summary>
        /// 通过目标URL获取代理配置
        /// </summary>
        public static int GetProxyConfigByUrl(IDatabase redis, string targetUrl) {
            var ports = redis.SetMembers($"{Prefix}Ports");
            
            foreach (var port in ports) {
                var listenPort = (int)port;
                var storedUrl = redis.StringGet($"{Prefix}Config:{listenPort}");
                
                if (storedUrl == targetUrl) {
                    return listenPort;
                }
            }
            
            return -1;
        }

        /// <summary>
        /// 异步通过目标URL获取代理配置
        /// </summary>
        public static async Task<int> GetProxyConfigByUrlAsync(IDatabaseAsync redis, string targetUrl) {
            var ports = await redis.SetMembersAsync($"{Prefix}Ports");
            
            foreach (var port in ports) {
                var listenPort = (int)port;
                var storedUrl = await redis.StringGetAsync($"{Prefix}Config:{listenPort}");
                
                if (storedUrl == targetUrl) {
                    return listenPort;
                }
            }
            
            return -1;
        }

        /// <summary>
        /// 获取目标URL的完整代理URL
        /// </summary>
        public static async Task<string> GetProxyUrlAsync(IDatabaseAsync redis, string targetUrl) {
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri)) {
                throw new ArgumentException("Invalid target URL format");
            }

            // 提取基础URL(不带路径)
            string baseUrl = $"{uri.Scheme}://{uri.Authority}";
            
            // 获取代理端口
            int port = await GetProxyConfigByUrlAsync(redis, baseUrl);
            if (port == -1) {
                throw new Exception("Proxy config not found for this URL");
            }
            
            // 构建带路径的本地URL
            return $"http://localhost:{port}{uri.PathAndQuery}";
        }

        /// <summary>
        /// 添加代理配置并返回完整代理URL(自动分配随机端口)
        /// </summary>
        public static async Task<string> GetProxyUrlWithRandomPortAsync(IDatabaseAsync redis, string targetUrl) {
            var result = await AddProxyConfigWithRandomPortAndPathAsync(redis, targetUrl);
            return result.LocalUrl;
        }
    }
}

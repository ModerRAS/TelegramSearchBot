using StackExchange.Redis;
using System;

namespace TelegramSearchBot.Helper {
    /// <summary>
    /// HTTP反向代理Redis配置工具
    /// </summary>
    public static class HttpProxyHelper {
        private const string Prefix = "TelegramSearchBot:HttpProxy:";

        /// <summary>
        /// 添加或更新HTTP反向代理配置
        /// </summary>
        /// <param name="redis">Redis数据库连接</param>
        /// <param name="listenPort">监听端口</param>
        /// <param name="targetUrl">目标服务器地址</param>
        /// <example>
        /// // 添加配置示例
        /// var redis = ConnectionMultiplexer.Connect("localhost:6379");
        /// HttpProxyHelper.AddProxyConfig(redis.GetDatabase(), 8080, "http://example.com");
        /// </example>
        public static void AddProxyConfig(IDatabase redis, int listenPort, string targetUrl) {
            // 验证目标URL格式
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out _)) {
                throw new ArgumentException("Invalid target URL format");
            }

            // 添加端口到集合
            redis.SetAdd($"{Prefix}Ports", listenPort);
            
            // 设置端口到目标的映射
            redis.StringSet($"{Prefix}Config:{listenPort}", targetUrl);
        }

        /// <summary>
        /// 移除HTTP反向代理配置
        /// </summary>
        /// <param name="redis">Redis数据库连接</param>
        /// <param name="listenPort">要移除的监听端口</param>
        public static void RemoveProxyConfig(IDatabase redis, int listenPort) {
            // 从端口集合移除
            redis.SetRemove($"{Prefix}Ports", listenPort);
            
            // 删除映射配置
            redis.KeyDelete($"{Prefix}Config:{listenPort}");
        }
    }
}

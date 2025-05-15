using StackExchange.Redis;
using System;
using System.Threading.Tasks;
using TelegramSearchBot.Helper;

namespace TelegramSearchBot.Extension {
    /// <summary>
    /// IDatabase异步扩展方法
    /// </summary>
    public static class IDatabaseAsyncExtension {
        public static async Task<string> StringWaitGetDeleteAsync(this IDatabaseAsync db, string key) {
            while (true) {
                if (!(await db.StringGetAsync(key)).Equals(RedisValue.Null)) {
                    return (await db.StringGetDeleteAsync(key)).ToString();
                } else {
                    await Task.Delay(1000);
                }
            }
        }

        public static async Task AddHttpProxyConfigAsync(this IDatabaseAsync db, int listenPort, string targetUrl) {
            await HttpProxyHelper.AddProxyConfigAsync(db, listenPort, targetUrl);
        }

        public static async Task<int> AddHttpProxyConfigWithRandomPortAsync(this IDatabaseAsync db, string targetUrl) {
            return await HttpProxyHelper.AddProxyConfigWithRandomPortAsync(db, targetUrl);
        }

        public static async Task RemoveHttpProxyConfigAsync(this IDatabaseAsync db, int listenPort) {
            await HttpProxyHelper.RemoveProxyConfigAsync(db, listenPort);
        }

        public static async Task<int> GetHttpProxyConfigByUrlAsync(this IDatabaseAsync db, string targetUrl) {
            return await HttpProxyHelper.GetProxyConfigByUrlAsync(db, targetUrl);
        }

        public static async Task<string> GetProxyUrlWithRandomPortAsync(this IDatabaseAsync db, string targetUrl) {
            return await HttpProxyHelper.GetProxyUrlWithRandomPortAsync(db, targetUrl);
        }
    }

    /// <summary>
    /// IDatabase同步扩展方法
    /// </summary>
    public static class IDatabaseExtension {
        public static void AddHttpProxyConfig(this IDatabase db, int listenPort, string targetUrl) {
            HttpProxyHelper.AddProxyConfig(db, listenPort, targetUrl);
        }

        public static int AddHttpProxyConfigWithRandomPort(this IDatabase db, string targetUrl) {
            return HttpProxyHelper.AddProxyConfigWithRandomPort(db, targetUrl);
        }

        public static void RemoveHttpProxyConfig(this IDatabase db, int listenPort) {
            HttpProxyHelper.RemoveProxyConfig(db, listenPort);
        }

        public static int GetHttpProxyConfigByUrl(this IDatabase db, string targetUrl) {
            return HttpProxyHelper.GetProxyConfigByUrl(db, targetUrl);
        }
    }
}

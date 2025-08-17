using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramSearchBot.Extension
{
    /// <summary>
    /// Redis数据库扩展方法
    /// </summary>
    public static class RedisExtensions
    {
        /// <summary>
        /// 等待并获取删除字符串值
        /// </summary>
        /// <param name="db">Redis数据库</param>
        /// <param name="key">键名</param>
        /// <param name="timeout">超时时间</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>字符串值</returns>
        public static async Task<string> StringWaitGetDeleteAsync(
            this IDatabase db, 
            string key, 
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            timeout ??= TimeSpan.FromMinutes(5);
            var startTime = DateTime.UtcNow;
            
            while (DateTime.UtcNow - startTime < timeout)
            {
                var value = await db.StringGetAsync(key);
                if (value.HasValue)
                {
                    await db.KeyDeleteAsync(key);
                    return value.ToString();
                }
                
                await Task.Delay(100, cancellationToken);
            }
            
            throw new TimeoutException($"等待键 {key} 超时");
        }
    }
}
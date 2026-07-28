using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace TelegramSearchBot.Extension {
    public static class IDatabaseAsyncExtension {
        /// <summary>
        /// Waits for a Redis key to appear and returns its value, then deletes the key.
        /// Uses a polling approach with configurable timeout (default 30 seconds).
        /// </summary>
        /// <param name="db">The IDatabaseAsync instance.</param>
        /// <param name="key">The Redis key to wait for.</param>
        /// <param name="timeout">Maximum time to wait. Defaults to 30 seconds if null.</param>
        /// <param name="cancellationToken">Cancellation token to abort the operation.</param>
        /// <returns>The string value of the key.</returns>
        /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
        /// <exception cref="TimeoutException">Thrown when the timeout elapses before the key appears.</exception>
        public static async Task<string> StringWaitGetDeleteAsync(
            this IDatabaseAsync db,
            string key,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            var startTime = DateTime.UtcNow;

            while (true) {
                cancellationToken.ThrowIfCancellationRequested();

                var elapsed = DateTime.UtcNow - startTime;
                if (elapsed > effectiveTimeout) {
                    throw new TimeoutException($"Timeout waiting for Redis key: {key}");
                }

                if (!( await db.StringGetAsync(key) ).Equals(RedisValue.Null)) {
                    // Note: check-then-delete is not atomic but acceptable here since only one consumer polls this key
                    return ( await db.StringGetDeleteAsync(key) ).ToString();
                } else {
                    await Task.Delay(500, cancellationToken);
                }
            }
        }
    }
}

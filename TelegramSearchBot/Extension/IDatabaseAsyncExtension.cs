using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Extension {
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
    }
}

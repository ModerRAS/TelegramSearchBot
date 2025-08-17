using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Extension;
using TelegramSearchBot.Common;

namespace TelegramSearchBot.Service.Abstract
{
    public class SubProcessService : IService
    {
        public string ServiceName => "SubProcessService";
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }
        protected string ForkName { get; set; }
        public SubProcessService(IConnectionMultiplexer connectionMultiplexer)
        {
            this.connectionMultiplexer = connectionMultiplexer;
        }
        public async Task<string> RunRpc(string payload)
        {
            // 简化实现：暂时注释掉AppBootstrap相关代码
            var db = connectionMultiplexer.GetDatabase();
            var guid = Guid.NewGuid();
            await db.ListRightPushAsync($"{ForkName}Tasks", $"{guid}");
            await db.StringSetAsync($"{ForkName}Post-{guid}", payload);
            // await AppBootstrap.AppBootstrap.RateLimitForkAsync([ForkName, $"{Env.SchedulerPort}"]);
            return await db.StringWaitGetDeleteAsync($"{ForkName}Result-{guid}");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Extension;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Service.Abstract {
    [Injectable(ServiceLifetime.Transient)]
    public class SubProcessService : IService {
        public string ServiceName => "SubProcessService";
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }
        protected string ForkName { get; set; }
        public SubProcessService(IConnectionMultiplexer connectionMultiplexer) {
            this.connectionMultiplexer = connectionMultiplexer;
        }
        public async Task<string> RunRpc(string payload) {
            var db = connectionMultiplexer.GetDatabase();
            var guid = Guid.NewGuid();
            Log.Information("RunRpc started: ForkName={ForkName}, guid={guid}, payloadLen={payloadLen}",
                ForkName, guid, payload?.Length ?? -1);
            try {
                await db.StringSetAsync($"{ForkName}Post-{guid}", payload);
                await db.ListRightPushAsync($"{ForkName}Tasks", $"{guid}");
                await AppBootstrap.AppBootstrap.RateLimitForkAsync([ForkName, $"{Env.SchedulerPort}"]);
                var result = await db.StringWaitGetDeleteAsync($"{ForkName}Result-{guid}");
                Log.Information("RunRpc completed: ForkName={ForkName}, guid={guid}, resultLen={resultLen}",
                    ForkName, guid, result?.Length ?? -1);
                return result;
            } catch (Exception ex) {
                Log.Error(ex, "RunRpc failed: ForkName={ForkName}, guid={guid}", ForkName, guid);
                throw;
            }
        }
    }
}

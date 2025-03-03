using StackExchange.Redis;
using System;
using System.Threading.Tasks;
using TelegramSearchBot.Extension;

namespace TelegramSearchBot.Service {
    public class AutoQRService : SubProcessService {
        public AutoQRService(IConnectionMultiplexer connectionMultiplexer) : base(connectionMultiplexer) {
            ForkName = "QR";
        }

        public new string ServiceName => "AutoQRService";
        

        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public async Task<string> ExecuteAsync(string path) {
            var db = connectionMultiplexer.GetDatabase();
            var guid = Guid.NewGuid();
            await db.ListRightPushAsync("QRTasks", $"{guid}");
            await db.StringSetAsync($"QRPhotoImg-{guid}", path);
            await AppBootstrap.AppBootstrap.RateLimitForkAsync(["QR", $"{Env.SchedulerPort}"]);
            return await db.StringWaitGetDeleteAsync($"QRPhotoText-{guid}");
        }
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
    }
}

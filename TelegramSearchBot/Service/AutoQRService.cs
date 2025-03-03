using SkiaSharp;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Extension;
using TelegramSearchBot.Intrerface;
using ZXing.SkiaSharp;

namespace TelegramSearchBot.Service {
    public class AutoQRService : IService {
        public string ServiceName => "AutoQRService";
        public IConnectionMultiplexer connectionMultiplexer { get; set; }
        public AutoQRService(IConnectionMultiplexer connectionMultiplexer) {
            this.connectionMultiplexer = connectionMultiplexer;
        }

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

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
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Service {
    public class AutoASRService : IService {
        public string ServiceName => "AutoASRService";
        public WhisperManager WhisperManager { get; set; }
        public IConnectionMultiplexer connectionMultiplexer { get; set; }
        public AutoASRService(IConnectionMultiplexer connectionMultiplexer) {
            this.connectionMultiplexer = connectionMultiplexer;
        }


        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(Stream file) {
            if (!Env.EnableAutoASR) {
                return "";
            }
            using MemoryStream memstream = new MemoryStream();
            await file.CopyToAsync(memstream);

            var db = connectionMultiplexer.GetDatabase();
            var guid = Guid.NewGuid();
            await db.ListRightPushAsync("ASRTasks", $"{guid}");
            await db.StringSetAsync($"ASRAudioStream-{guid}", Convert.ToBase64String(memstream.ToArray()));
            await AppBootstrap.AppBootstrap.RateLimitForkAsync(["ASR", $"{Env.SchedulerPort}"]);
            return await db.StringWaitGetDeleteAsync($"ASRAudioText-{guid}");
        }

        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(string path) {
            var db = connectionMultiplexer.GetDatabase();
            var guid = Guid.NewGuid();
            await db.ListRightPushAsync("ASRTasks", $"{guid}");
            await db.StringSetAsync($"ASRAudioStream-{guid}", path);
            await AppBootstrap.AppBootstrap.RateLimitForkAsync(["ASR", $"{Env.SchedulerPort}"]);
            return await db.StringWaitGetDeleteAsync($"ASRAudioText-{guid}");
        }
    }
}

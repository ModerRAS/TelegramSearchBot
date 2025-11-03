using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore.Extend;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;
using TelegramSearchBot.Core.Interface.Controller;
using TelegramSearchBot.Manager;
using Whisper.net;
using Whisper.net.Ggml;

namespace TelegramSearchBot.AppBootstrap {
    public class ASRBootstrap : AppBootstrap {
        public static async Task Process(string[] args) {
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect($"localhost:{args[1]}");
            IDatabase db = redis.GetDatabase();
            var asr = new WhisperManager(new LoggerFactory().AddSerilog().CreateLogger<WhisperManager>());
            var before = DateTime.UtcNow;
            while (DateTime.UtcNow - before < TimeSpan.FromMinutes(10) ||
                   db.ListLength("ASRTasks") > 0) {
                if (db.ListLength("ASRTasks") == 0) {
                    Task.Delay(1000).Wait();
                    continue;
                }

                var task = db.ListLeftPop("ASRTasks").ToString();
                var audioPath = db.StringGetDelete($"ASRPost-{task}").ToString();
                var wave = await IProcessAudio.ConvertToWav(audioPath);
                using MemoryStream stream = new MemoryStream(wave);
                var response = await asr.DetectAsync(stream);
                db.StringSet($"ASRResult-{task}", response);
            }
        }
        public static void Startup(string[] args) {
            Process(args).Wait();
        }
    }
}

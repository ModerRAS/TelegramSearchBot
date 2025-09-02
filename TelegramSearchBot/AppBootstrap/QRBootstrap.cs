using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Markdig.Helpers;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.AppBootstrap {
    public class QRBootstrap : AppBootstrap {
        public static async Task Process(string[] args) {
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect($"localhost:{args[1]}");
            IDatabase db = redis.GetDatabase();
            var qr = new QRManager(new LoggerFactory().AddSerilog().CreateLogger<QRManager>());
            var before = DateTime.UtcNow;
            while (DateTime.UtcNow - before < TimeSpan.FromMinutes(10) ||
                   db.ListLength("QRTasks") > 0) {
                if (db.ListLength("QRTasks") == 0) {
                    Task.Delay(1000).Wait();
                    continue;
                }
                var task = db.ListLeftPop("QRTasks").ToString();
                var photoPath = db.StringGetDelete($"QRPost-{task}").ToString();
                string response = string.Empty;
                try {
                    response = await qr.ExecuteAsync(photoPath);
                } catch (Exception ex) {
                }

                db.StringSet($"QRResult-{task}", response);
            }
        }
        public static void Startup(string[] args) {
            Process(args).Wait();
        }
    }
}

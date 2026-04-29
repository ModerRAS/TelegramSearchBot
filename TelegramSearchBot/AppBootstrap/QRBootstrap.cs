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
                if (string.IsNullOrWhiteSpace(task)) {
                    Log.Logger.Warning("Empty task from QRTasks queue — skipping");
                    continue;
                }
                var photoPath = db.StringGetDelete($"QRPost-{task}").ToString();
                if (string.IsNullOrWhiteSpace(photoPath)) {
                    Log.Logger.Warning("Empty QRPost key for task {task} — data not yet available or already consumed", task);
                    continue;
                }
                string response = string.Empty;
                Log.Logger.Information("QR processing started: task={task}, path={path}", task, photoPath);
                try {
                    response = await qr.ExecuteAsync(photoPath);
                    Log.Logger.Information("QR result: task={task}, len={len}, content={preview}", task, response?.Length ?? -1, response?.Length > 100 ? response.Substring(0, 100) + "..." : response);
                } catch (Exception ex) {
                    Log.Logger.Warning(ex, "QR processing failed for task {task}", task);
                }

                db.StringSet($"QRResult-{task}", response);
            }
        }
        public static void Startup(string[] args) {
            Process(args).Wait();
        }
    }
}

using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.AppBootstrap {
    public class OCRBootstrap : AppBootstrap {
        public static void Startup(string[] args) {
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect($"localhost:{args[1]}");
            IDatabase db = redis.GetDatabase();
            var ocr = new PaddleOCR();
            var before = DateTime.UtcNow;
            while (DateTime.UtcNow - before < TimeSpan.FromMinutes(10)) {
                if (db.ListLength("OCRTasks") == 0) {
                    Task.Delay(1000).Wait();
                    continue;
                }
                var task = db.ListLeftPop("OCRTasks").ToString();
                var photoBase64 = db.StringGetDelete($"OCRPhotoImg-{task}").ToString();
                var response = ocr.Execute(new List<string>() { photoBase64 });
                int status;
                if (int.TryParse(response.Status, out status) && status == 0) {
                    var StringList = new List<string>();
                    foreach (var e in response.Results) {
                        foreach (var f in e) {
                            StringList.Add(f.Text);
                        }
                    }
                    db.StringSet($"OCRPhotoText-{task}", string.Join(" ", StringList));
                } else {
                    db.StringSet($"OCRPhotoText-{task}", "");
                }
            }
        }
    }
}

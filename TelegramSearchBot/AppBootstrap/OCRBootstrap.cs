using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Manager;
using Newtonsoft.Json;

namespace TelegramSearchBot.AppBootstrap {
    public class OCRBootstrap : AppBootstrap {
        public static void Startup(string[] args) {
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect($"localhost:{args[1]}");
            IDatabase db = redis.GetDatabase();
            var ocr = new PaddleOCR();
            var before = DateTime.UtcNow;
            while (DateTime.UtcNow - before < TimeSpan.FromMinutes(10) ||
                   db.ListLength("OCRCoordinatesTasks") > 0) {
                
                // 统一处理带坐标的OCR任务
                if (db.ListLength("OCRCoordinatesTasks") > 0) {
                    var task = db.ListLeftPop("OCRCoordinatesTasks").ToString();
                    var photoBase64 = db.StringGetDelete($"OCRCoordinatesPost-{task}").ToString();
                    var response = ocr.Execute(new List<string>() { photoBase64 });
                    
                    // 序列化完整的OCR结果为JSON
                    var jsonResult = JsonConvert.SerializeObject(response);
                    db.StringSet($"OCRCoordinatesResult-{task}", jsonResult);
                } else {
                    // 如果没有任务，稍微等待一下
                    Task.Delay(1000).Wait();
                }
            }
        }
    }
}

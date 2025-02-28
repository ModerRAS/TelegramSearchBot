using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading;
using SkiaSharp;
using TelegramSearchBot.Model;
using TelegramSearchBot.Common.Model.DO;
using TelegramSearchBot.Common.Model.DTO;
using TelegramSearchBot.Manager;
using StackExchange.Redis;
using Nito.AsyncEx;

namespace TelegramSearchBot.Service {
    public class PaddleOCRService : IStreamService, IService {
        public string ServiceName => "PaddleOCRService";

        private static readonly AsyncLock _asyncLock = new AsyncLock();
        public static DateTime DateTime { get; private set; } = DateTime.MinValue;
        public IConnectionMultiplexer connectionMultiplexer { get; set; }

        public PaddleOCRService(IConnectionMultiplexer connectionMultiplexer) {
            this.connectionMultiplexer = connectionMultiplexer;
        }


        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(Stream file) {
            if (!Env.EnableAutoOCR) {
                return "";
            }
            //var stream = new MemoryStream();
            
            var tg_img = SKBitmap.Decode(file);
            var tg_img_data = tg_img.Encode(SKEncodedImageFormat.Jpeg, 99);
            //tg_img.Save(stream, ImageFormat.Jpeg);
            var tg_img_arr = tg_img_data.ToArray();
            var tg_img_base64 = Convert.ToBase64String(tg_img_arr);
            var db = connectionMultiplexer.GetDatabase();
            var guid = Guid.NewGuid();
            await db.ListRightPushAsync("OCRTasks", $"{guid}");
            await db.StringSetAsync($"OCRPhotoImg-{guid}", tg_img_base64);
            using (await _asyncLock.LockAsync()) {
                if (DateTime.UtcNow - DateTime > TimeSpan.FromMinutes(5)) {
                    AppBootstrap.AppBootstrap.Fork(["OCR", $"{Env.SchedulerPort}"]);
                    DateTime = DateTime.UtcNow;
                }
            }
            while (true) {
                if (!db.StringGetAsync($"OCRPhotoText-{guid}").Equals(RedisValue.Null)) {
                    return (await db.StringGetDeleteAsync($"OCRPhotoText-{guid}")).ToString();
                } else {
                    await Task.Delay(1000);
                }
            }
            
            //var response = await PaddleOCR.ExecuteAsync(new List<string>() { tg_img_base64 });
            //int status;
            //if (int.TryParse(response.Status, out status) && status == 0) {
            //    var StringList = new List<string>();
            //    foreach (var e in response.Results) {
            //        foreach (var f in e) {
            //            StringList.Add(f.Text);
            //        }
            //    }
            //    return string.Join(" ", StringList);
            //} else {
            //    return "";
            //}
        }
    }
}

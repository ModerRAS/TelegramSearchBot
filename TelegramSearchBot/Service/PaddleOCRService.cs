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

namespace TelegramSearchBot.Service {
    public class PaddleOCRService : IStreamService, IService {
        public string ServiceName => "PaddleOCRService";
        public JobManager<OCRTaskPost, OCRTaskResult> JobManager { get; set; }
        public PaddleOCR PaddleOCR { get; set; }

        public PaddleOCRService(JobManager<OCRTaskPost, OCRTaskResult> jobManager, PaddleOCR paddleOCR) {
            JobManager = jobManager;
            PaddleOCR = paddleOCR;
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
            var response = await PaddleOCR.ExecuteAsync(new List<string>() { tg_img_base64 });
            int status;
            if (int.TryParse(response.Status, out status) && status == 0) {
                var StringList = new List<string>();
                foreach (var e in response.Results) {
                    foreach (var f in e) {
                        StringList.Add(f.Text);
                    }
                }
                return string.Join(" ", StringList);
            } else {
                return "";
            }
        }
    }
}

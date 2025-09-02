using System;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface.AI.OCR;
using TelegramSearchBot.Service.Abstract;

namespace TelegramSearchBot.Service.AI.OCR {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class PaddleOCRService : SubProcessService, IPaddleOCRService {
        public new string ServiceName => "PaddleOCRService";


        public PaddleOCRService(IConnectionMultiplexer connectionMultiplexer) : base(connectionMultiplexer) {
            ForkName = "OCR";
        }

        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(Stream file) {
            var tg_img = SKBitmap.Decode(file);
            var tg_img_data = tg_img.Encode(SKEncodedImageFormat.Jpeg, 99);
            var tg_img_arr = tg_img_data.ToArray();
            var tg_img_base64 = Convert.ToBase64String(tg_img_arr);
            return await RunRpc(tg_img_base64);

        }
    }
}

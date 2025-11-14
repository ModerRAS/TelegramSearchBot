using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
        /// 处理图片OCR识别
        /// </summary>
        /// <param name="file">图片文件流</param>
        /// <returns>识别的文本内容</returns>
        public async Task<string> ExecuteAsync(Stream file) {
            var tg_img = SKBitmap.Decode(file);
            var tg_img_data = tg_img.Encode(SKEncodedImageFormat.Jpeg, 99);
            var tg_img_arr = tg_img_data.ToArray();
            var tg_img_base64 = Convert.ToBase64String(tg_img_arr);
            
            // 调用独立OCR服务
            var resultJson = await RunRpc(tg_img_base64);
            
            if (string.IsNullOrEmpty(resultJson)) {
                return string.Empty;
            }
            
            try {
                // 解析OCR结果
                var ocrResult = JsonConvert.DeserializeObject<TelegramSearchBot.Common.Model.DO.PaddleOCRResult>(resultJson);
                if (ocrResult?.Results != null && ocrResult.Results.Count > 0) {
                    var textResults = new System.Collections.Generic.List<string>();
                    foreach (var resultList in ocrResult.Results) {
                        if (resultList != null) {
                            textResults.AddRange(resultList.Select(r => r.Text));
                        }
                    }
                    return string.Join(" ", textResults.Where(t => !string.IsNullOrWhiteSpace(t)));
                }
            } catch (Exception ex) {
                // 记录解析错误，但不抛出异常
                Console.WriteLine($"OCR结果解析失败: {ex.Message}");
            }
            
            return string.Empty;
        }
    }
}
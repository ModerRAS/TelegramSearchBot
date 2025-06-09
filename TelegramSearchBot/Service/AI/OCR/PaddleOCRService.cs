using System;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Service.Abstract;
using TelegramSearchBot.Interface.AI.OCR;
using TelegramSearchBot.Common.Model.DO;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace TelegramSearchBot.Service.AI.OCR {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class PaddleOCRService : SubProcessService, IPaddleOCRService {
        public new string ServiceName => "PaddleOCRService";


        public PaddleOCRService(IConnectionMultiplexer connectionMultiplexer) : base(connectionMultiplexer) {
            ForkName = "OCR";
        }

        /// <summary>
        /// 执行OCR识别，返回文本内容（向后兼容方法）
        /// </summary>
        /// <param name="file">图片文件流</param>
        /// <returns>识别的文本内容</returns>
        public async Task<string> ExecuteAsync(Stream file) {
            // 内部调用带坐标的方法，然后提取文本
            var fullResult = await ExecuteWithCoordinatesAsync(file);
            
            if (fullResult?.Results != null) {
                var textList = new List<string>();
                foreach (var resultGroup in fullResult.Results) {
                    foreach (var result in resultGroup) {
                        if (!string.IsNullOrWhiteSpace(result.Text)) {
                            textList.Add(result.Text);
                        }
                    }
                }
                return string.Join(" ", textList);
            }
            
            return string.Empty;
        }

        /// <summary>
        /// 执行OCR识别，返回包含文字和坐标信息的完整结果
        /// </summary>
        /// <param name="file">图片文件流</param>
        /// <returns>包含文字、坐标和置信度的OCR结果</returns>
        public async Task<PaddleOCRResult> ExecuteWithCoordinatesAsync(Stream file) {
            var tg_img = SKBitmap.Decode(file);
            var tg_img_data = tg_img.Encode(SKEncodedImageFormat.Jpeg, 99);
            var tg_img_arr = tg_img_data.ToArray();
            var tg_img_base64 = Convert.ToBase64String(tg_img_arr);
            
            // 使用统一的坐标任务处理
            var resultJson = await RunRpcWithCoordinates(tg_img_base64);
            
            // 反序列化JSON结果
            var result = JsonConvert.DeserializeObject<PaddleOCRResult>(resultJson);
            return result;
        }

        private async Task<string> RunRpcWithCoordinates(string base64Image) {
            var id = Guid.NewGuid().ToString();
            
            // 使用统一的坐标队列
            connectionMultiplexer.GetDatabase().StringSet($"OCRCoordinatesPost-{id}", base64Image);
            connectionMultiplexer.GetDatabase().ListRightPush("OCRCoordinatesTasks", id);

            while (true) {
                var result = connectionMultiplexer.GetDatabase().StringGetDelete($"OCRCoordinatesResult-{id}");
                if (result.HasValue) {
                    return result;
                }
                await Task.Delay(100);
            }
        }
    }
}

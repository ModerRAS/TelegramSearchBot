using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using TelegramSearchBot.Common.Model;
using TelegramSearchBot.Common.Model.DO;

namespace TelegramSearchBot.OCR {
    /// <summary>
    /// OCR处理服务，提供图片文字识别功能
    /// </summary>
    public class OCRService {
        private readonly ILogger<OCRService> _logger;
        private readonly PaddleOcrAll _ocrAll;
        private static readonly SemaphoreSlim _semaphore = new(1);

        public OCRService(ILogger<OCRService> logger) {
            _logger = logger;
            
            try {
                var model = LocalFullModels.ChineseV3;
                _ocrAll = new PaddleOcrAll(model, PaddleDevice.Mkldnn()) {
                    AllowRotateDetection = true,
                    Enable180Classification = false,
                };
                
                _logger.LogInformation("OCR模型初始化成功");
            } catch (Exception ex) {
                _logger.LogError(ex, "OCR模型初始化失败");
                throw;
            }
        }

        /// <summary>
        /// 处理图片OCR识别
        /// </summary>
        /// <param name="base64Image">Base64编码的图片</param>
        /// <returns>OCR识别结果</returns>
        public PaddleOCRResult ProcessImage(string base64Image) {
            try {
                var imageBytes = Convert.FromBase64String(base64Image);
                
                using (Mat src = Cv2.ImDecode(imageBytes, ImreadModes.Color)) {
                    var ocrResult = _ocrAll.Run(src);
                    var results = ConvertToResults(ocrResult);
                    
                    return new PaddleOCRResult {
                        Results = new List<List<Result>> { results },
                        Status = "0",
                        Message = ""
                    };
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "OCR处理失败");
                return new PaddleOCRResult {
                    Results = new List<List<Result>>(),
                    Status = "1",
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// 异步处理图片OCR识别
        /// </summary>
        /// <param name="base64Image">Base64编码的图片</param>
        /// <returns>OCR识别结果</returns>
        public async Task<PaddleOCRResult> ProcessImageAsync(string base64Image) {
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try {
                var result = ProcessImage(base64Image);
                return result;
            } finally {
                _semaphore.Release();
            }
        }

        private List<Result> ConvertToResults(PaddleOcrResult paddleOcrResult) {
            var results = new List<Result>();
            foreach (var region in paddleOcrResult.Regions) {
                results.Add(new Result {
                    Text = region.Text,
                    TextRegion = region.Rect.Points().Select(point => {
                        return new List<int> { (int)point.X, (int)point.Y };
                    }).ToList(),
                    Confidence = float.IsNaN(region.Score) ? 0 : region.Score,
                });
            }
            return results;
        }
    }
}
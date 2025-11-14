using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using TelegramSearchBot.Common.Model;
using TelegramSearchBot.Common.Model.DO;
using TelegramSearchBot.OCR;  // 引用新的OCR服务

namespace TelegramSearchBot.Manager {
    /// <summary>
    /// 与原有PaddleOCR类完全兼容的实现
    /// 提供相同的接口和行为，但内部实现委托给新的OCRService
    /// </summary>
    public class PaddleOCR {
        private readonly OCRService _ocrService;
        private static readonly SemaphoreSlim _semaphore = new(1);

        // 保持原有的公共属性
        public PaddleOcrAll all { get; set; }

        public PaddleOCR() {
            // 初始化OCR服务
            var loggerFactory = LoggerFactory.Create(builder => {
                builder.AddConsole();
                builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
            });
            
            _ocrService = new OCRService(loggerFactory.CreateLogger<OCRService>());
            
            // 保持原有的PaddleOcrAll初始化，用于兼容性
            try {
                FullOcrModel model = LocalFullModels.ChineseV3;
                all = new PaddleOcrAll(model, PaddleDevice.Mkldnn()) {
                    AllowRotateDetection = true,
                    Enable180Classification = false,
                };
            } catch (Exception ex) {
                // 如果初始化失败，保持all为null，但OCR服务仍然可以工作
                Console.WriteLine($"PaddleOcrAll初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理单张图片（保持原有接口）
        /// </summary>
        public PaddleOcrResult GetOcrResult(byte[] image) {
            using (Mat src = Cv2.ImDecode(image, ImreadModes.Color)) {
                PaddleOcrResult result = all.Run(src);
                return result;
            }
        }

        /// <summary>
        /// 转换PaddleOcrResult到Results（保持原有接口）
        /// </summary>
        public List<Result> ConvertToResults(PaddleOcrResult paddleOcrResult) {
            var results = new List<Result>();
            foreach (var region in paddleOcrResult.Regions) {
                results.Add(new Result {
                    Text = region.Text,
                    TextRegion = region.Rect.Points().Select(point => {
                        return new List<int>() { (int)point.X, (int)point.Y };
                    }).ToList(),
                    Confidence = float.IsNaN(region.Score) ? 0 : region.Score,
                });
            }
            return results;
        }

        /// <summary>
        /// 处理多张图片（保持原有接口，但内部使用新的OCR服务）
        /// </summary>
        public PaddleOCRResult Execute(List<string> images) {
            try {
                // 使用新的OCR服务处理图片
                var results = new List<List<Result>>();
                
                foreach (var imageBase64 in images) {
                    var result = _ocrService.ProcessImage(imageBase64);
                    if (result?.Results != null && result.Results.Count > 0) {
                        results.AddRange(result.Results);
                    }
                }
                
                return new PaddleOCRResult() {
                    Results = results,
                    Status = "0",
                    Message = "",
                };
            } catch (Exception ex) {
                // 发生错误时返回空结果，保持与原有行为一致
                Console.WriteLine($"OCR处理错误: {ex.Message}");
                return new PaddleOCRResult() {
                    Results = new List<List<Result>>(),
                    Status = "1",
                    Message = ex.Message,
                };
            }
        }

        /// <summary>
        /// 异步处理多张图片（保持原有接口）
        /// </summary>
        public async Task<PaddleOCRResult> ExecuteAsync(List<string> images) {
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try {
                var results = await Task.Run<PaddleOCRResult>(() => Execute(images));
                return results;
            } finally {
                _semaphore.Release();
            }
        }
    }
}
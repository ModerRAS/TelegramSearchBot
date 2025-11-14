using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TelegramSearchBot.Common.Model;
using TelegramSearchBot.Common.Model.DO;

namespace TelegramSearchBot.OCR.Services {
    public class OCRBootstrapService {
        private static ILogger<OCRBootstrapService> _logger;

        public static async Task StartAsync(string[] args) {
            try {
                // 创建简单的日志工厂
                var loggerFactory = LoggerFactory.Create(builder => {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
                
                _logger = loggerFactory.CreateLogger<OCRBootstrapService>();
                _logger.LogInformation($"OCRBootstrapService启动，参数: {string.Join(", ", args)}");
                
                // 连接到Redis，使用传入的端口参数
                var redisPort = args.Length > 1 ? args[1] : "6379";
                var redisConnection = $"localhost:{redisPort}";
                
                _logger.LogInformation($"正在连接到Redis: {redisConnection}");
                ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnection);
                IDatabase db = redis.GetDatabase();
                
                _logger.LogInformation("Redis连接成功");
                
                // 初始化OCR处理服务
                var ocrService = new OCRProcessingService(loggerFactory.CreateLogger<OCRProcessingService>());
                
                var before = DateTime.UtcNow;
                var processedCount = 0;
                
                _logger.LogInformation("开始处理OCR任务...");
                
                // 保持原有的运行逻辑：运行10分钟或直到队列为空
                while (DateTime.UtcNow - before < TimeSpan.FromMinutes(10) ||
                       db.ListLength("OCRTasks") > 0) {
                    
                    if (db.ListLength("OCRTasks") == 0) {
                        await Task.Delay(1000);
                        continue;
                    }
                    
                    // 从队列获取任务
                    var task = db.ListLeftPop("OCRTasks").ToString();
                    var photoBase64 = db.StringGetDelete($"OCRPost-{task}").ToString();
                    
                    if (string.IsNullOrEmpty(photoBase64)) {
                        continue;
                    }
                    
                    try {
                        _logger.LogInformation($"开始处理OCR任务: {task}");
                        
                        // 使用新的OCR服务处理图片
                        var result = await ocrService.ProcessImageAsync(photoBase64);
                        
                        // 提取文本结果，保持与原有格式兼容
                        if (result?.Results != null && result.Results.Count > 0) {
                            var textResults = new List<string>();
                            foreach (var resultList in result.Results) {
                                if (resultList != null) {
                                    textResults.AddRange(resultList.Select(r => r.Text));
                                }
                            }
                            var finalText = string.Join(" ", textResults.Where(t => !string.IsNullOrWhiteSpace(t)));
                            db.StringSet($"OCRResult-{task}", finalText, TimeSpan.FromMinutes(10));
                            
                            processedCount++;
                            _logger.LogInformation($"OCR任务 {task} 处理完成，识别到 {textResults.Count} 个文本区域");
                        } else {
                            db.StringSet($"OCRResult-{task}", "", TimeSpan.FromMinutes(10));
                            _logger.LogWarning($"OCR任务 {task} 未识别到文本");
                        }
                    } catch (Exception ex) {
                        // 发生错误时返回空结果，保持与原有行为一致
                        _logger.LogError(ex, $"OCR任务 {task} 处理失败");
                        db.StringSet($"OCRResult-{task}", "", TimeSpan.FromMinutes(10));
                    }
                }
                
                _logger.LogInformation($"OCRBootstrapService处理完成，共处理 {processedCount} 个任务");
                
                // 关闭Redis连接
                redis.Close();
                
            } catch (Exception ex) {
                _logger?.LogError(ex, "OCRBootstrapService启动失败");
                Console.WriteLine($"OCRBootstrapService启动失败: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }
    }
}
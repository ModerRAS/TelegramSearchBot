using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TelegramSearchBot.Common.Model.DO;

namespace TelegramSearchBot.OCR.Services {
    public class OCRWorkerService : BackgroundService {
        private readonly ILogger<OCRWorkerService> _logger;
        private readonly OCRProcessingService _ocrService;
        private IConnectionMultiplexer _redis;
        private IDatabase _db;

        public OCRWorkerService(ILogger<OCRWorkerService> logger, OCRProcessingService ocrService) {
            _logger = logger;
            _ocrService = ocrService;
        }

        public override async Task StartAsync(CancellationToken cancellationToken) {
            try {
                // 从环境变量或配置获取Redis连接信息
                var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
                var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
                var redisConnection = $"{redisHost}:{redisPort}";
                
                _redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
                _db = _redis.GetDatabase();
                
                _logger.LogInformation($"OCR工作服务启动，连接到Redis: {redisConnection}");
                
                await base.StartAsync(cancellationToken);
            } catch (Exception ex) {
                _logger.LogError(ex, "OCR工作服务启动失败");
                throw;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _logger.LogInformation("OCR工作服务开始运行");
            
            while (!stoppingToken.IsCancellationRequested) {
                try {
                    // 检查OCR任务队列
                    var taskCount = await _db.ListLengthAsync("OCRTasks");
                    
                    if (taskCount == 0) {
                        // 队列为空，等待一段时间
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }
                    
                    // 从队列获取任务
                    var taskId = await _db.ListLeftPopAsync("OCRTasks");
                    if (string.IsNullOrEmpty(taskId)) {
                        continue;
                    }
                    
                    // 获取图片数据
                    var imageKey = $"OCRPost-{taskId}";
                    var imageBase64 = await _db.StringGetDeleteAsync(imageKey);
                    
                    if (string.IsNullOrEmpty(imageBase64)) {
                        _logger.LogWarning($"未找到任务 {taskId} 的图片数据");
                        continue;
                    }
                    
                    _logger.LogInformation($"开始处理OCR任务: {taskId}");
                    
                    // 处理OCR
                    var result = await _ocrService.ProcessImageAsync(imageBase64);
                    
                    // 保存结果
                    var resultKey = $"OCRResult-{taskId}";
                    var resultJson = JsonConvert.SerializeObject(result);
                    await _db.StringSetAsync(resultKey, resultJson, TimeSpan.FromMinutes(10));
                    
                    _logger.LogInformation($"OCR任务 {taskId} 处理完成");
                    
                } catch (Exception ex) {
                    _logger.LogError(ex, "处理OCR任务时发生错误");
                    // 等待一段时间后继续
                    await Task.Delay(5000, stoppingToken);
                }
            }
            
            _logger.LogInformation("OCR工作服务停止运行");
        }

        public override async Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("OCR工作服务正在停止...");
            
            if (_redis != null) {
                await _redis.CloseAsync();
                _redis.Dispose();
            }
            
            await base.StopAsync(cancellationToken);
            _logger.LogInformation("OCR工作服务已停止");
        }
    }
}
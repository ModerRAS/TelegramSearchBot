using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.OCR.Services;

namespace TelegramSearchBot.OCR {
    /// <summary>
    /// 兼容原有OCRBootstrap的启动类
    /// 主进程通过反射调用此类的Startup方法
    /// </summary>
    public class OCRBootstrap {
        private static ILogger<OCRBootstrap> _logger;

        public static void Startup(string[] args) {
            try {
                // 创建简单的日志工厂
                var loggerFactory = LoggerFactory.Create(builder => {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
                
                _logger = loggerFactory.CreateLogger<OCRBootstrap>();
                _logger.LogInformation($"OCRBootstrap启动，参数: {string.Join(", ", args)}");
                
                // 运行OCR引导服务
                OCRBootstrapService.StartAsync(args).GetAwaiter().GetResult();
                
                _logger.LogInformation("OCRBootstrap正常退出");
            } catch (Exception ex) {
                Console.WriteLine($"OCRBootstrap启动失败: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }
    }
}
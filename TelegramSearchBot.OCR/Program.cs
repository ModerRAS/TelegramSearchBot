using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.OCR.Services;

namespace TelegramSearchBot.OCR {
    public class Program {
        public static async Task Main(string[] args) {
            // 检查是否是作为OCRBootstrap被主进程调用
            if (args.Length >= 2 && args[0] == "OCR") {
                // 保持原有的OCRBootstrap逻辑，但使用新的OCR服务
                try {
                    Console.WriteLine($"OCR服务启动，参数: {string.Join(", ", args)}");
                    await OCRBootstrapService.StartAsync(args);
                    Console.WriteLine("OCR服务正常退出");
                } catch (Exception ex) {
                    Console.WriteLine($"OCR服务运行失败: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                    Environment.Exit(1);
                }
            } else {
                // 正常的独立服务模式（Docker模式）
                try {
                    var host = Host.CreateDefaultBuilder(args)
                        .ConfigureServices((hostContext, services) => {
                            services.AddHostedService<OCRWorkerService>();
                            services.AddSingleton<OCRProcessingService>();
                        })
                        .ConfigureLogging(logging => {
                            logging.AddConsole();
                            logging.SetMinimumLevel(LogLevel.Information);
                        })
                        .Build();

                    await host.RunAsync();
                } catch (Exception ex) {
                    Console.WriteLine($"独立服务模式运行失败: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                    Environment.Exit(1);
                }
            }
        }
    }
}
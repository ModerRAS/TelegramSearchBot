using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.OCR.Services;

namespace TelegramSearchBot.OCR {
    public class Program {
        public static async Task Main(string[] args) {
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
        }
    }
}
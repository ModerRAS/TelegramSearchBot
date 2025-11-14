using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Extension;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Service.Abstract {
    public class SubProcessService : IService {
        public string ServiceName => "SubProcessService";
        protected IConnectionMultiplexer connectionMultiplexer { get; set; }
        protected string ForkName { get; set; }
        
        public SubProcessService(IConnectionMultiplexer connectionMultiplexer) {
            this.connectionMultiplexer = connectionMultiplexer;
        }
        
        public async Task<string> RunRpc(string payload) {
            var db = connectionMultiplexer.GetDatabase();
            var guid = Guid.NewGuid();
            await db.ListRightPushAsync($"{ForkName}Tasks", $"{guid}");
            await db.StringSetAsync($"{ForkName}Post-{guid}", payload);
            
            // 启动对应的子进程
            await StartSubProcessIfNeeded();
            
            return await db.StringWaitGetDeleteAsync($"{ForkName}Result-{guid}");
        }
        
        private async Task StartSubProcessIfNeeded() {
            if (ForkName == "OCR") {
                // 启动独立的OCR服务
                await StartOCRService();
            } else {
                // 其他服务保持原有的fork机制
                await AppBootstrap.AppBootstrap.RateLimitForkAsync([ForkName, $"{Env.SchedulerPort}"]);
            }
        }
        
        private async Task StartOCRService() {
            try {
                // 获取当前主进程的路径
                var mainExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(mainExePath)) {
                    // 如果无法获取主进程路径，回退到原有的fork机制
                    await AppBootstrap.AppBootstrap.RateLimitForkAsync([ForkName, $"{Env.SchedulerPort}"]);
                    return;
                }
                
                // 构建OCR服务路径（与主进程在同一目录）
                var ocrExePath = Path.Combine(Path.GetDirectoryName(mainExePath), "TelegramSearchBot.OCR");
                
                // 检查OCR服务是否存在
                if (!File.Exists(ocrExePath)) {
                    // 如果OCR服务不存在，回退到原有的fork机制
                    Console.WriteLine($"OCR服务未找到: {ocrExePath}，使用内置OCR");
                    await AppBootstrap.AppBootstrap.RateLimitForkAsync([ForkName, $"{Env.SchedulerPort}"]);
                    return;
                }
                
                // 启动独立的OCR服务
                await AppBootstrap.AppBootstrap.RateLimitForkAsync(ocrExePath, new string[] { ForkName, $"{Env.SchedulerPort}" });
                Console.WriteLine($"已启动独立OCR服务: {ocrExePath}");
                
            } catch (Exception ex) {
                Console.WriteLine($"启动OCR服务失败: {ex.Message}，回退到内置OCR");
                // 发生错误时回退到原有的fork机制
                await AppBootstrap.AppBootstrap.RateLimitForkAsync([ForkName, $"{Env.SchedulerPort}"]);
            }
        }
    }
}
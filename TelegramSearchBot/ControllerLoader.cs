using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Intrerface;

namespace TelegramSearchBot {
    static class ControllerLoader {
        public static void AddController(IServiceCollection service) {
            service.AddSingleton<SearchNextPageController>();//这一段这两行更适合用反射来加载
            service.AddSingleton<MessageController>();
            service.AddSingleton<SearchController>();
            service.AddSingleton<SendMessage>();
            service.AddSingleton<ImportController>();
            service.AddSingleton<RefreshController>();
            service.AddSingleton<AutoQRController>();
        }
        public static void InitController(IServiceProvider service) {
            _ = service.GetRequiredService<SearchNextPageController>();
            _ = service.GetRequiredService<MessageController>();
            _ = service.GetRequiredService<SearchController>();
            _ = service.GetRequiredService<ImportController>();
            _ = service.GetRequiredService<RefreshController>();
            _ = service.GetRequiredService<AutoQRController>();
            _ = service.GetRequiredService<SendMessage>().Run();
        }
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TelegramSearchBot.AIApi {
    public class Startup {
        public static void BootStrap(string[] args) {


            var builder = WebApplication.CreateBuilder(args);

            // 1. 在服务容器中注册 Controllers 服务
            builder.Services.AddControllers();

            var app = builder.Build();

            // 配置 HTTP 请求管道
            if (app.Environment.IsDevelopment()) {
                app.UseDeveloperExceptionPage(); // 开发环境下使用开发者异常页面
            }

            // 2. 配置路由中间件 - 必须在 UseEndpoints 或 MapControllers 之前调用
            app.UseRouting();

            // 3. 配置端点映射 - 启用 Controllers 的路由
            // 这会查找项目中的 Controller 并根据路由配置映射请求到对应的 Action
            app.MapControllers();

            // 注意：如果你之前使用了 app.MapGet("/", ...) 等 Minimal API 端点，
            // 现在改为使用 Controllers，可以将这些 Minimal API 的行删除或注释掉。

            app.Run();
        }
    }
}

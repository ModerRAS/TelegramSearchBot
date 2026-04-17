using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Tools;

namespace TelegramSearchBot.LLMAgent {
    public static class LLMAgentProgram {
        public static async Task RunAsync(string[] args) {
            var effectiveArgs = NormalizeArgs(args);
            if (effectiveArgs.Length != 2 ||
                !long.TryParse(effectiveArgs[0], out var chatId) ||
                !int.TryParse(effectiveArgs[1], out var port)) {
                Console.Error.WriteLine("Usage: LLMAgent <chatId> <port>");
                Environment.ExitCode = 1;
                return;
            }

            using var services = BuildServices(port);
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("LLMAgent");
            McpToolHelper.EnsureInitialized(
                typeof(Service.AgentToolService).Assembly,
                typeof(FileToolService).Assembly,
                services, logger);

            var loop = services.GetRequiredService<Service.AgentLoopService>();
            using var shutdownCts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => {
                eventArgs.Cancel = true;
                shutdownCts.Cancel();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdownCts.Cancel();

            await loop.RunAsync(chatId, port, shutdownCts.Token);
        }

        private static string[] NormalizeArgs(string[] args) {
            if (args.Length > 0 && args[0].Equals("LLMAgent", StringComparison.OrdinalIgnoreCase)) {
                return args.Skip(1).ToArray();
            }

            return args;
        }

        private static ServiceProvider BuildServices(int port) {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddSimpleConsole(options => {
                options.SingleLine = true;
                options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
            }));
            services.AddHttpClient();
            services.AddHttpClient("OllamaClient");
            services.AddDbContext<DataDbContext>(options => {
                options.UseInMemoryDatabase($"llm-agent-{port}");
            }, contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect($"localhost:{port},abortConnect=false,connectTimeout=5000,connectRetry=5"));
            services.AddScoped<IMessageExtensionService, Service.InMemoryMessageExtensionService>();
            services.AddScoped<OpenAIService>();
            services.AddScoped<OllamaService>();
            services.AddScoped<GeminiService>();
            services.AddScoped<AnthropicService>();
            services.AddScoped<Service.ToolExecutor>();
            services.AddScoped<Service.AgentToolService>();
            services.AddScoped<IFileToolService, FileToolService>();
            services.AddScoped<IBashToolService, BashToolService>();
            services.AddScoped<Service.IAgentTaskExecutor, Service.LlmServiceProxy>();
            services.AddScoped<Service.LlmServiceProxy>();
            services.AddSingleton<Service.GarnetClient>();
            services.AddSingleton<Service.GarnetRpcClient>();
            services.AddSingleton<Service.AgentLoopService>();
            return services.BuildServiceProvider();
        }
    }
}

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
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Tools;

namespace TelegramSearchBot.LLMAgent {
    public static class LLMAgentProgram {
        public static async Task RunAsync(string[] args) {
            var effectiveArgs = NormalizeArgs(args);
            if (effectiveArgs.Length > 0 && effectiveArgs[0].Equals("SandboxToolHost", StringComparison.OrdinalIgnoreCase)) {
                await RunSandboxToolHostAsync(effectiveArgs.Skip(1).ToArray());
                return;
            }

            if (effectiveArgs.Length != 2 ||
                !long.TryParse(effectiveArgs[0], out var chatId) ||
                !int.TryParse(effectiveArgs[1], out var port)) {
                Console.Error.WriteLine("Usage: LLMAgent <chatId> <port> | SandboxToolHost <chatId> <port> <boxName> <parentPid> <parentStartTicksUtc>");
                Environment.ExitCode = 1;
                return;
            }

            using var services = BuildServices(port);
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("LLMAgent");
            McpToolHelper.EnsureInitialized(
                typeof(Service.AgentToolService).Assembly,
                services, logger);

            var loop = services.GetRequiredService<Service.AgentLoopService>();
            using var shutdownCts = CreateShutdownTokenSource();

            await loop.RunAsync(chatId, port, shutdownCts.Token);
        }

        private static async Task RunSandboxToolHostAsync(string[] args) {
            if (args.Length != 5 ||
                !long.TryParse(args[0], out var chatId) ||
                !int.TryParse(args[1], out var port) ||
                !int.TryParse(args[3], out var parentProcessId) ||
                !long.TryParse(args[4], out var parentStartTicksUtc)) {
                Console.Error.WriteLine("Usage: SandboxToolHost <chatId> <port> <boxName> <parentPid> <parentStartTicksUtc>");
                Environment.ExitCode = 1;
                return;
            }

            var boxName = args[2];
            using var services = BuildServices(port);
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SandboxToolHost");
            McpToolHelper.EnsureInitialized(
                typeof(FileToolService).Assembly,
                services, logger);

            using var shutdownCts = CreateShutdownTokenSource();
            var consumer = services.GetRequiredService<Service.SandboxToolConsumer>();
            await consumer.RunAsync(chatId, boxName, parentProcessId, parentStartTicksUtc, shutdownCts.Token);
        }

        private static CancellationTokenSource CreateShutdownTokenSource() {
            var shutdownCts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => {
                eventArgs.Cancel = true;
                shutdownCts.Cancel();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdownCts.Cancel();
            return shutdownCts;
        }

        private static string[] NormalizeArgs(string[] args) {
            if (args.Length > 0 && args[0].Equals("LLMAgent", StringComparison.OrdinalIgnoreCase)) {
                return args.Skip(1).ToArray();
            }

            return args;
        }

        private static ServiceProvider BuildServices(int port) {
            var services = new ServiceCollection();
            services.AddLogging(builder => {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddSimpleConsole(options => {
                    options.SingleLine = true;
                    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                });
            });
            services.AddHttpClient();
            services.AddHttpClient("OllamaClient");
            services.AddDbContext<DataDbContext>(options => {
                options.UseInMemoryDatabase($"llm-agent-{port}");
            }, contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect($"localhost:{port},abortConnect=false,connectTimeout=5000,connectRetry=5"));
            services.AddScoped<IMessageExtensionService, Service.InMemoryMessageExtensionService>();
            services.AddSingleton<IBotIdentityProvider, BotIdentityProvider>();
            services.AddScoped<IGroupLlmSettingsService, GroupLlmSettingsService>();
            services.AddScoped<OpenAIService>();
            services.AddScoped<OpenAIResponsesService>();
            services.AddScoped<OllamaService>();
            services.AddScoped<GeminiService>();
            services.AddScoped<AnthropicService>();
            services.AddSingleton<Service.ToolExecutor>();
            services.AddScoped<Service.AgentToolService>();
            services.AddScoped<IFileToolService, FileToolService>();
            services.AddScoped<IBashToolService, BashToolService>();
            services.AddScoped<Service.IAgentTaskExecutor, Service.LlmServiceProxy>();
            services.AddScoped<Service.LlmServiceProxy>();
            services.AddSingleton<Service.GarnetClient>();
            services.AddSingleton<Service.GarnetRpcClient>();
            services.AddSingleton<Service.AgentToolRegistryService>();
            services.AddSingleton<Service.AgentLoopService>();
            services.AddSingleton<Service.SandboxToolConsumer>();
            return services.BuildServiceProvider();
        }
    }
}

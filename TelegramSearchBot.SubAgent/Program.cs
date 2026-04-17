using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace TelegramSearchBot.SubAgent {
    public static class Program {
        public static async Task Main(string[] args) {
            var effectiveArgs = args.Length > 0 && args[0].Equals("SubAgent", StringComparison.OrdinalIgnoreCase)
                ? args.Skip(1).ToArray()
                : args;

            if (effectiveArgs.Length != 1 || !int.TryParse(effectiveArgs[0], out var port)) {
                Console.Error.WriteLine("Usage: SubAgent <port>");
                Environment.ExitCode = 1;
                return;
            }

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddSimpleConsole(options => {
                options.SingleLine = true;
                options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
            }));
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect($"localhost:{port},abortConnect=false,connectTimeout=5000,connectRetry=5"));
            services.AddSingleton<Service.SubAgentService>();

            using var provider = services.BuildServiceProvider();
            using var shutdownCts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => {
                eventArgs.Cancel = true;
                shutdownCts.Cancel();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdownCts.Cancel();

            await provider.GetRequiredService<Service.SubAgentService>().RunAsync(shutdownCts.Token);
        }
    }
}

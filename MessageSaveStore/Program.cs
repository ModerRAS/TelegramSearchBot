using AgileConfig.Client;
using MessageSaveStore.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Common;

namespace MessageSaveStore {
    internal class Program {
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureHostConfiguration(config => {
                    config.AddEnvironmentVariables();
                })
                .UseAgileConfig()
                .ConfigureAppConfiguration((context, config) => {
                    try {

                        var configClient = new ConfigClient(
                            Env.AgileConfigAppId, 
                            Env.AgileConfigSecret, 
                            Env.AgileConfigServerNodes, 
                            Env.AgileConfigEnv
                            );

                        //注册配置项修改事件
                        configClient.ConfigChanged += (arg) => {
                            Console.WriteLine($"action:{arg.Action} key:{arg.Key}");
                        };
                        config.AddAgileConfig(configClient);
                    } catch (ArgumentNullException ex) {
                        Console.WriteLine("Could not load Agile Config");
                    }


                    //使用AddAgileConfig配置一个新的IConfigurationSource

                })
                .ConfigureServices((context, service) => {
                    service.AddAgileConfig();
                    service.AddTransient<ILogger, ILogger>();
                    service.AddTransient<StoreService>();
                });
        static void Main(string[] args) {
            IHost host = CreateHostBuilder(args)
                .ConfigureLogging(logging =>
                logging.AddFilter("System", LogLevel.Warning)
                  .AddFilter("Microsoft", LogLevel.Warning))
                .Build();
        }
    }
}
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramSearchBot.AppBootstrap
{
    public class HttpProxyBootstrap : AppBootstrap
    {
        private static ILogger<HttpProxyBootstrap> _logger;
        private static IDatabase _redisDb;
        private static Dictionary<int, IWebHost> _activeServers = new Dictionary<int, IWebHost>();
        private static CancellationTokenSource _cts = new CancellationTokenSource();
        private const string Prefix = "TelegramSearchBot:HttpProxy:";

        public static async Task Process(string[] args)
        {
            // 初始化Redis连接
            var redis = ConnectionMultiplexer.Connect($"localhost:{args[1]}");
            _redisDb = redis.GetDatabase();
            _logger = new LoggerFactory().AddSerilog().CreateLogger<HttpProxyBootstrap>();

            // 初始加载配置
            await HandleConfigChangeAsync();

            // 轮询检查配置变更
            while (!_cts.Token.IsCancellationRequested)
            {
                await HandleConfigChangeAsync();
                await Task.Delay(500); // 每5秒检查一次配置变更
            }
        }

        private static async Task HandleConfigChangeAsync()
        {
            var ports = _redisDb.SetMembers($"{Prefix}Ports");
            var currentPorts = _activeServers.Keys.ToList();

            // 处理新增端口
            foreach (var port in ports)
            {
                var listenPort = int.Parse(port);
                if (!_activeServers.ContainsKey(listenPort))
                {
                    await StartServerAsync(listenPort);
                }
            }

            // 处理移除的端口
            foreach (var port in currentPorts)
            {
                if (!ports.Any(p => int.Parse(p) == port))
                {
                    await StopServerAsync(port);
                }
            }
        }

        private static async Task StartServerAsync(int listenPort)
        {
            var configKey = $"{Prefix}Config:{listenPort}";
            _logger.LogInformation($"Starting HTTP proxy server on port {listenPort}");

            var host = new WebHostBuilder()
                .UseKestrel()
                .ConfigureServices(services =>
                {
                    services.AddHttpClient();
                })
                .Configure(app =>
                {
                    app.Run(async context =>
                    {
                        try
                        {
                            // 从Redis获取目标服务器地址
                            var targetServer = _redisDb.StringGet(configKey);
                            if (targetServer.IsNullOrEmpty)
                            {
                                context.Response.StatusCode = 502;
                                await context.Response.WriteAsync("No target server configured");
                                return;
                            }

                            // 创建带代理的HttpClientHandler
                            var httpClientHandler = new HttpClientHandler
                            {
                                UseProxy = true,
                                Proxy = WebRequest.GetSystemWebProxy(),
                                UseDefaultCredentials = true,
                            };

                            // 创建新HttpClient实例
                            using var httpClient = new HttpClient(httpClientHandler);
                            httpClient.BaseAddress = new Uri(targetServer);

                            // 构造转发请求
                            var requestMessage = new HttpRequestMessage
                            {
                                Method = new HttpMethod(context.Request.Method),
                                RequestUri = new Uri(httpClient.BaseAddress, context.Request.Path + context.Request.QueryString)
                            };

                            // 复制请求头
                            foreach (var header in context.Request.Headers)
                            {
                                // 不复制 Host，因为 HttpClient 会自动根据 URI 设置正确的 Host
                                if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase)) {
                                    continue;
                                }
                                    
                                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                                {
                                    requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                                }
                            }

                            // 复制请求体(如果有)
                            if (context.Request.ContentLength > 0)
                            {
                                requestMessage.Content = new StreamContent(context.Request.Body);
                                if (!string.IsNullOrEmpty(context.Request.ContentType))
                                {
                                    requestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(context.Request.ContentType);
                                }
                            }

                            // 转发请求并返回响应
                            var responseMessage = await httpClient.SendAsync(requestMessage);
                            context.Response.StatusCode = (int)responseMessage.StatusCode;
                            foreach (var header in responseMessage.Headers)
                            {
                                context.Response.Headers[header.Key] = header.Value.ToArray();
                            }
                            await responseMessage.Content.CopyToAsync(context.Response.Body);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "HTTP Proxy error");
                            context.Response.StatusCode = 500;
                            await context.Response.WriteAsync($"Proxy error: {ex.Message}");
                        }
                    });
                })
                .UseUrls($"http://*:{listenPort}")
                .Build();

            _activeServers[listenPort] = host;
            _ = host.RunAsync();
        }

        private static async Task StopServerAsync(int listenPort)
        {
            if (_activeServers.TryGetValue(listenPort, out var host))
            {
                _logger.LogInformation($"Stopping HTTP proxy server on port {listenPort}");
                await host.StopAsync();
                _activeServers.Remove(listenPort);
            }
        }

        public static void Startup(string[] args)
        {
            Process(args).Wait();
        }
    }
}

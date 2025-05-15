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
                .UseKestrel(options =>
                {
                    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
                    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
                })
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
                            var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                            context.Response.StatusCode = (int)responseMessage.StatusCode;
                            
                            // 复制所有响应头(包括Transfer-Encoding)
                            foreach (var header in responseMessage.Headers)
                            {
                                context.Response.Headers[header.Key] = header.Value.ToArray();
                            }
                            
                            // 复制内容头
                            foreach (var header in responseMessage.Content.Headers)
                            {
                                context.Response.Headers[header.Key] = header.Value.ToArray();
                            }
                            
                            // 检查是否为SSE或流式响应
                            bool isStreaming = responseMessage.Content.Headers.ContentType?.MediaType == "text/event-stream" ||
                                              responseMessage.Headers.TransferEncodingChunked == true ||
                                              (responseMessage.Content.Headers.ContentLength == null && 
                                               responseMessage.Content.Headers.ContentType != null);
                            
                            if (isStreaming)
                            {
                                // 对于SSE，保持连接并流式传输
                                _logger.LogInformation($"Starting SSE proxy for {targetServer}");
                                _logger.LogDebug($"Request Headers: {string.Join(", ", context.Request.Headers.Select(h => $"{h.Key}={h.Value}"))}");
                                
                                context.Response.Headers["Cache-Control"] = "no-cache";
                                context.Response.Headers["Connection"] = "keep-alive";
                                await context.Response.StartAsync();
                                
                                try
                                {
                                    using var stream = await responseMessage.Content.ReadAsStreamAsync();
                                    var buffer = new byte[8192];
                                    int bytesRead;
                                    long totalBytes = 0;
                                    
                                    while (true)
                                    {
                                        try
                                        {
                                            bytesRead = await stream.ReadAsync(buffer, context.RequestAborted);
                                            if (bytesRead <= 0) break;
                                            
                                            await context.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), context.RequestAborted);
                                            await context.Response.Body.FlushAsync(context.RequestAborted);
                                            totalBytes += bytesRead;
                                            _logger.LogDebug($"Transferred {bytesRead} bytes (total: {totalBytes})");
                                            
                                            // 记录chunk数据前128字节用于调试
                                            if (responseMessage.Headers.TransferEncodingChunked == true && bytesRead > 0)
                                            {
                                                string chunkHex = BitConverter.ToString(buffer, 0, Math.Min(bytesRead, 128));
                                                _logger.LogTrace($"Chunk data (first 128 bytes): {chunkHex}");
                                            }
                                            
                                            // 防止CPU占用过高
                                            await Task.Delay(10, context.RequestAborted);
                                        }
                                        catch (HttpIOException hex) when (hex.Message.Contains("chunk extension"))
                                        {
                                            _logger.LogError($"Invalid chunk extension detected: {hex.Message}");
                                            throw;
                                        }
                                    }
                                    _logger.LogInformation($"SSE proxy completed. Total bytes: {totalBytes}");
                                }
                                // catch (TaskCanceledException)
                                // {
                                //     _logger.LogInformation($"SSE connection closed by client. Target: {targetServer}");
                                // }
                                catch (Exception ex)
                                {
                                    // 记录响应内容前1000字符用于调试
                                    string responseContent = "";
                                    try 
                                    {
                                        responseContent = await responseMessage.Content.ReadAsStringAsync();
                                        responseContent = responseContent.Length > 1000 ? responseContent.Substring(0, 1000) + "..." : responseContent;
                                    }
                                    catch {}
                                    
                                    _logger.LogError(ex, $"SSE stream error. Target: {targetServer}\n" +
                                        $"Response Headers: {string.Join(", ", responseMessage.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"))}\n" +
                                        $"Response Content (first 1000 chars): {responseContent}");
                                    throw;
                                }
                            }
                            else
                            {
                                // 普通响应处理
                                await responseMessage.Content.CopyToAsync(context.Response.Body);
                            }
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

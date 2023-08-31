using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using System.Threading;
using System.Timers;
using TelegramSearchBot.Common.Model.DO;
using TelegramSearchBot.Common.Model.DTO;

namespace TelegramSearchBot.Agent.PaddleOCR {
    internal class Program {
        static async Task<int> Main(string[] args) {
            using ILoggerFactory loggerFactory =
                LoggerFactory.Create(builder =>
                builder.AddSimpleConsole(options => {
                    options.IncludeScopes = true;
                    options.SingleLine = true;
                    options.TimestampFormat = "[yyyy/MM/dd HH:mm:ss] ";
                }));

            ILogger<Program> logger = loggerFactory.CreateLogger<Program>();
            var url = Environment.GetEnvironmentVariable("TelegramSearchBot.Agent.PaddleOCR.URL");
            var token = Environment.GetEnvironmentVariable("TelegramSearchBot.Agent.PaddleOCR.Token");
            if (url is null || token is null) {
                logger.LogError("Please Add Environment Variables `TelegramSearchBot.Agent.PaddleOCR.URL` for url and `TelegramSearchBot.Agent.PaddleOCR.Token` for token!");
                await Task.Delay(10_000);
                return 1;
            }
            var connection = new HubConnectionBuilder()
                .WithUrl(url)
                //.AddMessagePackProtocol()
                //.WithAutomaticReconnect()
                .Build();
            var client = new HttpClient();
            var rnd = new Random();
            var paddleOcr = new PaddleOCR();
            var IsBusy = false;
            var timer = new System.Timers.Timer(300_000);
            timer.AutoReset = true;
            timer.Enabled = true;
            timer.Elapsed += async (object? sender, ElapsedEventArgs e) => {
                logger.LogInformation("定时器触发了");
                if (!IsBusy) {
                    logger.LogInformation("自动断开连接重连中。。。。。");
                    await connection.StopAsync();
                    await connection.StartAsync();
                    await connection.SendAsync("GetJob", token);
                }
            };
            connection.KeepAliveInterval = TimeSpan.FromSeconds(1);
            connection.On<OCRTaskPost>("paddleocr", async (post) => {
                try {
                    logger.LogInformation($"{post.Id} {post.IsVaild}");
                    if (!post.IsVaild) {
                        await connection.StopAsync();
                    }
                    IsBusy = true;
                    var response = paddleOcr.Execute(post.PaddleOCRPost.Images);
                    IsBusy = false;
                    await connection.SendAsync("PostResult", token, new OCRTaskResult() { Id = post.Id, PaddleOCRResult = response });
                } catch (Exception ex) {
                    logger.LogError(ex.ToString());
                }
                await connection.SendAsync("GetJob", token);
            }); 
            connection.Closed += async (error) =>
            {
                logger.LogWarning("断线了");
                await Task.Delay(rnd.Next(0, 5) * 1000);
                await connection.StartAsync();
                await connection.SendAsync("GetJob", token);
            };

            await connection.StartAsync();
            await connection.SendAsync("GetJob", token);
            logger.LogInformation("启动完毕");
            while (true) {
                await Task.Delay(int.MaxValue);
            }
        }
    }
}
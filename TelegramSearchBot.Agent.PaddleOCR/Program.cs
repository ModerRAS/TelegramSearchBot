using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using System.Threading;
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
            if (args.Length != 2) {
                logger.LogError("TelegramSearchBot.Agent.PaddleOCR.exe <url> <token>");
                return 1;
            }
            var url = args[0];
            var token = args[1];
            var connection = new HubConnectionBuilder()
                .WithUrl(url)
                //.AddMessagePackProtocol()
                //.WithAutomaticReconnect()
                .Build();
            var client = new HttpClient();
            var rnd = new Random();
            var paddleOcr = new PaddleOCR();
            connection.On<OCRTaskPost>("paddleocr", async (post) => {
                try {
                    logger.LogInformation($"{post.Id} {post.IsVaild}");
                    if (!post.IsVaild) {
                        await connection.StopAsync();
                    }
                    var response = paddleOcr.Execute(post.PaddleOCRPost.Images);
                    await connection.SendAsync("PostResult", token, new OCRTaskResult() { Id = post.Id, PaddleOCRResult = response });
                } catch (Exception ex) {
                    logger.LogError(ex.ToString());
                }
                await connection.SendAsync("GetJob", token);
            }); 
            connection.Closed += async (error) =>
            {
                await Task.Delay(rnd.Next(0, 5) * 1000);
                await connection.StartAsync();
                await connection.SendAsync("GetJob", token);
            };

            await connection.StartAsync();
            await connection.SendAsync("GetJob", token);
            while (true) {
                await Task.Delay(int.MaxValue);
            }
        }
    }
}
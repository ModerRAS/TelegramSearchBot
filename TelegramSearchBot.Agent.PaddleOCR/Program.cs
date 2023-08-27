using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System.Text;
using System.Threading;
using TelegramSearchBot.Common.Model.DO;
using TelegramSearchBot.Common.Model.DTO;

namespace TelegramSearchBot.Agent.PaddleOCR {
    internal class Program {
        static async Task<int> Main(string[] args) {
            if (args.Length != 2) {
                Console.WriteLine("TelegramSearchBot.Agent.PaddleOCR.exe <url> <token>");
                return 1;
            }
            var url = args[0];
            var token = args[1];
            var connection = new HubConnectionBuilder()
                .WithUrl(url)
                //.WithAutomaticReconnect()
                .Build();
            var client = new HttpClient();
            var rnd = new Random();
            var paddleOcr = new PaddleOCR();
            connection.On<OCRTaskPost>("paddleocr", async (post) => {
                try {
                    Console.WriteLine($"{post.Id}");
                    var response = paddleOcr.Execute(post.PaddleOCRPost.Images);
                    await connection.SendAsync("PostResult", token, new OCRTaskResult() { Id = post.Id, PaddleOCRResult = response });
                } catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
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
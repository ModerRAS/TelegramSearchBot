using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using TelegramSearchBot.Common.Model.DTO;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Hubs {
    public class OCRHub : Hub {
        private JobManager<OCRTaskPost, OCRTaskResult> manager { get; init; }
        private ITokenManager tokenManager { get; init; }
        private ILogger<OCRHub> logger { get; set; }
        public OCRHub(JobManager<OCRTaskPost, OCRTaskResult> manager, ITokenManager tokenManager, ILogger<OCRHub> logger) {
            this.manager = manager;
            this.tokenManager = tokenManager;
            this.logger = logger;
        }
        public override Task OnDisconnectedAsync(Exception e) {
            if (e is not null) {
                logger.LogError($"Client {Context.ConnectionId} explicitly closed the connection. Exception {e}");
            }

            return base.OnDisconnectedAsync(e);
        }
        public async Task PostResult(string token, OCRTaskResult result) {
            if (await CheckToken(token)) {
                manager.Add(result);
            }
        }
        public async Task<bool> CheckToken(string token) {
            return tokenManager.CheckToken("OCRHub", token);
        }
        public async Task GetJob(string token) {
            if (await CheckToken(token)) {
                logger.LogInformation($"Accept {token} in OCRHub");
                var post = await manager.GetAsync();
                logger.LogInformation($"{token} Get a Job");
                await Clients.Caller.SendAsync("paddleocr", post);
            } else {
                logger.LogInformation($"Deny {token} in OCRHub");
                await Clients.Caller.SendAsync("paddleocr", new OCRTaskPost() { 
                    Id = Guid.NewGuid(),
                    PaddleOCRPost = new Common.Model.DO.PaddleOCRPost() { Images = new System.Collections.Generic.List<string>()},
                    IsVaild = false});
            }
        }
    }
}

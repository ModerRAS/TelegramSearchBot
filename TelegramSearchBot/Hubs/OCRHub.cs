using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using TelegramSearchBot.Common.Model.DTO;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Hubs {
    public class OCRHub : Hub {
        private JobManager<OCRTaskPost, OCRTaskResult> manager { get; init; }
        private ITokenManager tokenManager { get; init; }
        public OCRHub(JobManager<OCRTaskPost, OCRTaskResult> manager, ITokenManager tokenManager) {
            this.manager = manager;
            this.tokenManager = tokenManager;
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
                var post = await manager.GetAsync();
                await Clients.Caller.SendAsync("paddleocr", post);
            } else {
                await Clients.Caller.SendAsync("paddleocr", new OCRTaskPost() { 
                    Id = Guid.NewGuid(),
                    PaddleOCRPost = new Common.Model.DO.PaddleOCRPost() { Images = new System.Collections.Generic.List<string>()},
                    IsVaild = false});
            }
        }
    }
}

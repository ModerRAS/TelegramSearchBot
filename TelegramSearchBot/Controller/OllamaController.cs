using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;

namespace TelegramSearchBot.Controller {
    public class OllamaController : IOnUpdate {
        private readonly ILogger logger;
        public ITelegramBotClient botClient { get; set; }
        public OllamaController(ITelegramBotClient botClient, ILogger<OllamaController> logger) {
            this.logger = logger;
            this.botClient = botClient;

        }
        public async Task ExecuteAsync(Update e) {
            var BotName = (await botClient.GetMeAsync()).Username;
            var Message = string.IsNullOrEmpty(e.Message.Text) ? e.Message.Caption : e.Message.Text;
            if (Message.Contains(BotName)) {

            }
        }
    }
}

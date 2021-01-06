using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using NSonic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class RefreshController : IOnMessage {
        private readonly RefreshService refreshService;
        public RefreshController(SearchContext context, 
                                 IDistributedCache Cache,
                                 RefreshService refreshService, 
                                 SendMessage Send
            ) {
            this.refreshService = refreshService;
        }

        

        public async Task ExecuteAsync(object sender, MessageEventArgs e) {
            if (e.Message.Chat.Id < 0) {
                return;
            }
            if (e.Message.Chat.Id != Env.AdminId) {
                return;
            }
            string Command;
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                Command = e.Message.Text;
            } else if (!string.IsNullOrEmpty(e.Message.Caption)) {
                Command = e.Message.Caption;
            } else return;
            await refreshService.ExecuteAsync(Command);
        }

    }
}

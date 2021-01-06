using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;

namespace TelegramSearchBot.Intrerface {
    public interface IOnMessage {
        public Task ExecuteAsync(object sender, MessageEventArgs e);
    }
}

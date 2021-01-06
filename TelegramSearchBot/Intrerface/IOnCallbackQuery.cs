using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;

namespace TelegramSearchBot.Intrerface {
    public interface IOnCallbackQuery {
        public Task ExecuteAsync(object sender, CallbackQueryEventArgs e);
    }
}

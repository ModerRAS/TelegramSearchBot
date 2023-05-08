using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Common.Intrerface {
    public interface IOnCallbackQuery {
        public Task ExecuteAsync(Update e);
    }
}

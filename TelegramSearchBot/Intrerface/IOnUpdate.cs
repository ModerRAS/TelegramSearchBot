using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Intrerface {
    public interface IOnUpdate {
        public Task ExecuteAsync(Update e);
    }
}

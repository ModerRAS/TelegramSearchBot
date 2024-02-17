using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Intrerface {
    internal interface IPreUpdate {
        public Task ExecuteAsync(Update e);
    }
}

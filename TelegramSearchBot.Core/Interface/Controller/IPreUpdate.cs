using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Core.Interface.Controller {
    internal interface IPreUpdate {
        public Task ExecuteAsync(Update e);
    }
}

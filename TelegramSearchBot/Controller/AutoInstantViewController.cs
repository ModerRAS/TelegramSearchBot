using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;

namespace TelegramSearchBot.Controller {
    class AutoInstantViewController : IOnMessage {

        public Task ExecuteAsync(object sender, Telegram.Bot.Args.MessageEventArgs e) {
            throw new NotImplementedException();
        }
    }
}

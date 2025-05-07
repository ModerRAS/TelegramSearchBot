using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;

namespace TelegramSearchBot.Controller.Manage {
    public class EditLLMConfController : IOnUpdate {
        public List<Type> Dependencies => new List<Type>();

        public async Task ExecuteAsync(Update e) {
            
        }
    }
}

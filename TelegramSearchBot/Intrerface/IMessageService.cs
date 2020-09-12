using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Intrerface {
    public abstract class IMessageService {
        public abstract Task ExecuteAsync(MessageOption messageOption);

    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Interface {
    public interface IOnUpdate {
        List<Type> Dependencies { get; } // 每个Controller的依赖项

        public Task ExecuteAsync(Update e);
    }
}

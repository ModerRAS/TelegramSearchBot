using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Intrerface {
    public interface IMessageService : IService {
        public abstract Task ExecuteAsync(MessageOption messageOption);

    }
}

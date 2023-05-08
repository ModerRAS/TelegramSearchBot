using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Common.DTO;

namespace TelegramSearchBot.Common.Intrerface
{
    public interface IMessageService {
        public abstract Task ExecuteAsync(MessageOption messageOption);

    }
}

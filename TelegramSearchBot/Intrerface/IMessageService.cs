using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Intrerface {
    public interface IMessageService {
        public abstract Task ExecuteAsync(MessageOption messageOption);

    }
}

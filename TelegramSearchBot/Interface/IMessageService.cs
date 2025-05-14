using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Interface {
    public interface IMessageService {
        public abstract Task ExecuteAsync(MessageOption messageOption);

    }
}

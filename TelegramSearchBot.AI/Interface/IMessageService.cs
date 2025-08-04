using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Interface {
    public interface IMessageService {
        public abstract Task<long> ExecuteAsync(MessageOption messageOption);

    }
}

using System.Threading.Tasks;
using TelegramSearchBot.Core.Model;

namespace TelegramSearchBot.Core.Interface {
    public interface IMessageService {
        public abstract Task<long> ExecuteAsync(MessageOption messageOption);

    }
}

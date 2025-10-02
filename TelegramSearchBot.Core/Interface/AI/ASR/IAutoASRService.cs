using System.Threading.Tasks;

namespace TelegramSearchBot.Core.Interface.AI.ASR {
    public interface IAutoASRService {
        Task<string> ExecuteAsync(string path);
    }
}

using System.Threading.Tasks;

namespace TelegramSearchBot.Core.Interface.AI.QR {
    public interface IAutoQRService {
        Task<string> ExecuteAsync(string path);
    }
}

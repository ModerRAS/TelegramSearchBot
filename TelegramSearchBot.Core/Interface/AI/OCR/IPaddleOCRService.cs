using System.IO;
using System.Threading.Tasks;

namespace TelegramSearchBot.Core.Interface.AI.OCR {
    public interface IPaddleOCRService {
        Task<string> ExecuteAsync(Stream file);
    }
}

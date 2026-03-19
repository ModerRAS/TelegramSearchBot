using System.IO;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interface.AI.OCR {
    public enum OCREngine {
        PaddleOCR,
        LLM
    }

    public interface IOCRService {
        OCREngine Engine { get; }
        Task<string> ExecuteAsync(Stream file);
    }
}

using System.IO;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interface.AI.OCR
{
    public interface IPaddleOCRService
    {
        Task<string> ExecuteAsync(Stream file);
    }
}

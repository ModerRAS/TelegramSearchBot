using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools {
    public interface ISendPhotoToolService {
        Task<ToolResult> SendPhotoBase64Async(
            string base64,
            string caption,
            ToolContext toolContext);

        Task<ToolResult> SendPhotoFileAsync(
            string filePath,
            string caption,
            ToolContext toolContext);
    }
}

using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools {
    public interface ISendPhotoToolService {
        Task<SendPhotoResult> SendPhotoBase64(
            string base64Data,
            ToolContext toolContext,
            string caption = null,
            long? replyToMessageId = null);

        Task<SendPhotoResult> SendPhotoFile(
            string filePath,
            ToolContext toolContext,
            string caption = null,
            long? replyToMessageId = null);
    }
}

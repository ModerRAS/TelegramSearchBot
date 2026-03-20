using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools {
    public interface ISendVideoToolService {
        Task<SendVideoResult> SendVideoFile(
            string filePath,
            ToolContext toolContext,
            string caption = null,
            long? replyToMessageId = null);
    }
}

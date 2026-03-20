using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools {
    public interface ISendDocumentToolService {
        Task<SendDocumentResult> SendDocumentFile(
            string filePath,
            ToolContext toolContext,
            string caption = null,
            int? replyToMessageId = null);
    }
}

using System.Threading.Tasks;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Service.BotAPI;

namespace TelegramSearchBot.View {
    public class EditMcpConfView : IView {
        private readonly ISendMessageService _sendMessageService;

        private long _chatId;
        private int _replyToMessageId;
        private string _messageText;

        public EditMcpConfView(ISendMessageService sendMessageService) {
            _sendMessageService = sendMessageService;
        }

        public EditMcpConfView WithChatId(long chatId) {
            _chatId = chatId;
            return this;
        }

        public EditMcpConfView WithReplyTo(int messageId) {
            _replyToMessageId = messageId;
            return this;
        }

        public EditMcpConfView WithMessage(string message) {
            _messageText = message;
            return this;
        }

        public async Task Render() {
            await _sendMessageService.SplitAndSendTextMessage(
                _messageText,
                new Telegram.Bot.Types.Chat { Id = _chatId },
                _replyToMessageId);
        }
    }
}

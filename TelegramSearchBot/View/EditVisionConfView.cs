using System.Threading.Tasks;
using Telegram.Bot;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Service.BotAPI;

namespace TelegramSearchBot.View {
    public class EditVisionConfView : IView {
        private readonly ISendMessageService _sendMessageService;

        private long _chatId;
        private int _replyToMessageId;
        private string _messageText;

        public EditVisionConfView(ISendMessageService sendMessageService) {
            _sendMessageService = sendMessageService;
        }

        public EditVisionConfView WithChatId(long chatId) {
            _chatId = chatId;
            return this;
        }

        public EditVisionConfView WithReplyTo(int messageId) {
            _replyToMessageId = messageId;
            return this;
        }

        public EditVisionConfView WithMessage(string message) {
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

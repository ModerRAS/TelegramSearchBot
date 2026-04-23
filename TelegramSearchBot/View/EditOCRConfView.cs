using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.BotAPI;

namespace TelegramSearchBot.View {
    public class EditOCRConfView : IView {
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        private long _chatId;
        private int _replyToMessageId;
        private string _messageText = string.Empty;
        private ReplyMarkup? _replyMarkup;

        public EditOCRConfView(ITelegramBotClient botClient, SendMessage sendMessage) {
            _botClient = botClient;
            _sendMessage = sendMessage;
        }

        public EditOCRConfView WithChatId(long chatId) {
            _chatId = chatId;
            return this;
        }

        public EditOCRConfView WithReplyTo(int messageId) {
            _replyToMessageId = messageId;
            return this;
        }

        public EditOCRConfView WithMessage(string message) {
            _messageText = message;
            return this;
        }

        public EditOCRConfView WithReplyMarkup(ReplyMarkup? replyMarkup) {
            _replyMarkup = replyMarkup;
            return this;
        }

        public async Task Render() {
            await _sendMessage.AddTask(async () => await _botClient.SendMessage(
                chatId: _chatId,
                text: _messageText,
                replyParameters: new ReplyParameters { MessageId = _replyToMessageId },
                replyMarkup: _replyMarkup), _chatId < 0);
        }
    }
}

using System.Threading.Tasks;
using Telegram.Bot;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Service.BotAPI;

namespace TelegramSearchBot.View
{
    public class EditLLMConfView : IView
    {
        private readonly SendMessageService _sendMessageService;

        private long _chatId;
        private int _replyToMessageId;
        private string _messageText;

        public EditLLMConfView(SendMessageService sendMessageService)
        {
            _sendMessageService = sendMessageService;
        }

        public EditLLMConfView WithChatId(long chatId)
        {
            _chatId = chatId;
            return this;
        }

        public EditLLMConfView WithReplyTo(int messageId)
        {
            _replyToMessageId = messageId;
            return this;
        }

        public EditLLMConfView WithMessage(string message)
        {
            _messageText = message;
            return this;
        }

        public async Task Render()
        {
            await _sendMessageService.SplitAndSendTextMessage(
                _messageText,
                new Telegram.Bot.Types.Chat { Id = _chatId },
                _replyToMessageId);
        }
    }
}
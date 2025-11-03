using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Core.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Core.Model;

namespace TelegramSearchBot.View {
    public class GenericView : IView {
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        // ViewModel properties
        private long _chatId;
        private int _replyToMessageId;
        private string _textContent;
        private List<ViewButton> _buttons = new List<ViewButton>();
        private bool _disableNotification;

        public GenericView(ITelegramBotClient botClient, SendMessage sendMessage) {
            _botClient = botClient;
            _sendMessage = sendMessage;
        }

        public class ViewButton {
            public string Text { get; set; }
            public string CallbackData { get; set; }

            public ViewButton(string text, string callbackData) {
                Text = text;
                CallbackData = callbackData;
            }
        }

        // Fluent API methods
        public GenericView WithChatId(long chatId) {
            _chatId = chatId;
            return this;
        }

        public GenericView WithReplyTo(int messageId) {
            _replyToMessageId = messageId;
            return this;
        }

        public GenericView WithText(string text) {
            _textContent = MessageFormatHelper.ConvertMarkdownToTelegramHtml(text);
            return this;
        }

        public GenericView DisableNotification(bool disable = true) {
            _disableNotification = disable;
            return this;
        }

        public GenericView AddButton(string text, string callbackData) {
            _buttons.Add(new ViewButton(text, callbackData));
            return this;
        }

        public async Task Render() {
            var replyParameters = new Telegram.Bot.Types.ReplyParameters {
                MessageId = _replyToMessageId
            };

            var inlineButtons = _buttons?.Select(b =>
                InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToList();

            var replyMarkup = inlineButtons != null ?
                new InlineKeyboardMarkup(inlineButtons) : null;

            await _sendMessage.AddTaskWithResult(async () => await _botClient.SendMessage(
                chatId: _chatId,
                text: _textContent,
                parseMode: ParseMode.Html,
                replyParameters: replyParameters,
                disableNotification: _disableNotification,
                replyMarkup: replyMarkup
            ), _chatId);
        }
    }
}

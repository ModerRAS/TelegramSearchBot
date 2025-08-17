using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.View
{
    public class GenericView : IView
    {
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

        public class ViewButton
        {
            public string Text { get; set; }
            public string CallbackData { get; set; }
            
            public ViewButton(string text, string callbackData)
            {
                Text = text;
                CallbackData = callbackData;
            }
        }

        // Fluent API methods
        public IView WithChatId(long chatId)
        {
            _chatId = chatId;
            return this;
        }

        public IView WithReplyTo(int messageId)
        {
            _replyToMessageId = messageId;
            return this;
        }

        public IView WithText(string text)
        {
            _textContent = MessageFormatHelper.ConvertMarkdownToTelegramHtml(text);
            return this;
        }

        public IView WithCount(int count)
        {
            // GenericView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithSkip(int skip)
        {
            // GenericView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithTake(int take)
        {
            // GenericView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithSearchType(SearchType searchType)
        {
            // GenericView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithMessages(List<TelegramSearchBot.Model.Data.Message> messages)
        {
            // GenericView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithTitle(string title)
        {
            // GenericView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithHelp()
        {
            // GenericView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView DisableNotification(bool disable = true)
        {
            _disableNotification = disable;
            return this;
        }

        public IView WithMessage(string message)
        {
            // GenericView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithOwnerName(string ownerName)
        {
            // GenericView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public GenericView AddButton(string text, string callbackData)
        {
            _buttons.Add(new ViewButton(text, callbackData));
            return this;
        }

        public async Task Render()
        {
            var replyParameters = new Telegram.Bot.Types.ReplyParameters
            {
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
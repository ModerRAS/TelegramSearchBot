using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.View {
    public class StreamingView : IView {
        private readonly ITelegramBotClient _botClient;
        private readonly ISendMessageService _sendMessageService;
        private readonly SendMessage _sendMessage;

        // ViewModel properties
        private long _chatId;
        private int _replyToMessageId;
        private string _initialContent = "⏳";
        private IAsyncEnumerable<string> _streamData;
        private List<ViewButton> _buttons = new List<ViewButton>();
        private bool _disableNotification;
        private ParseMode _parseMode = ParseMode.Html;

        public StreamingView(ITelegramBotClient botClient, ISendMessageService sendMessageService, SendMessage sendMessage) {
            _botClient = botClient;
            _sendMessageService = sendMessageService;
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
        public IView WithChatId(long chatId) {
            _chatId = chatId;
            return this;
        }

        public IView WithReplyTo(int messageId) {
            _replyToMessageId = messageId;
            return this;
        }

        public IView WithText(string text) {
            // StreamingView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithCount(int count) {
            // StreamingView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithSkip(int skip) {
            // StreamingView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithTake(int take) {
            // StreamingView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithSearchType(SearchType searchType) {
            // StreamingView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithMessages(List<TelegramSearchBot.Model.Data.Message> messages) {
            // StreamingView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithTitle(string title) {
            // StreamingView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithHelp() {
            // StreamingView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        private async Task<Telegram.Bot.Types.Message> RenderFinalMessage(CancellationToken cancellationToken) {
            var inlineButtons = _buttons?.Select(b =>
                InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToList();

            return await _sendMessage.AddTaskWithResult(async () => await _botClient.SendMessage(
                chatId: _chatId,
                text: _initialContent,
                parseMode: _parseMode,
                replyParameters: new ReplyParameters { MessageId = _replyToMessageId },
                disableNotification: _disableNotification,
                replyMarkup: inlineButtons != null ? new InlineKeyboardMarkup(inlineButtons) : null
            ), _chatId);
        }

        public StreamingView WithInitialContent(string content) {
            _initialContent = content;
            return this;
        }

        public StreamingView WithStream(IAsyncEnumerable<string> streamData) {
            _streamData = streamData;
            return this;
        }

        public StreamingView WithParseMode(ParseMode parseMode) {
            _parseMode = parseMode;
            return this;
        }

        public IView DisableNotification(bool disable = true) {
            _disableNotification = disable;
            return this;
        }

        public IView WithMessage(string message) {
            // StreamingView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithOwnerName(string ownerName) {
            // StreamingView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public StreamingView AddButton(string text, string callbackData) {
            _buttons.Add(new ViewButton(text, callbackData));
            return this;
        }

        public async Task Render() {
            await Render(default);
        }

        public async Task<List<TelegramSearchBot.Model.Data.Message>> Render(CancellationToken cancellationToken = default) {
            if (_streamData == null) {
                return new List<TelegramSearchBot.Model.Data.Message>() { TelegramSearchBot.Model.Data.Message.FromTelegramMessage(await RenderFinalMessage(cancellationToken)) };
            }

            var inlineButtons = _buttons?.Select(b =>
                InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToList();

            var result = await _sendMessageService.SendFullMessageStream(
                _streamData,
                _chatId,
                _replyToMessageId,
                _initialContent,
                cancellationToken);
            return result;
        }
    }
}
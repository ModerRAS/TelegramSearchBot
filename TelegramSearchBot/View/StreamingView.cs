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
        public StreamingView WithChatId(long chatId) {
            _chatId = chatId;
            return this;
        }

        public StreamingView WithReplyTo(int messageId) {
            _replyToMessageId = messageId;
            return this;
        }

        private async Task<Message> RenderFinalMessage(CancellationToken cancellationToken) {
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

        public StreamingView DisableNotification(bool disable = true) {
            _disableNotification = disable;
            return this;
        }

        public StreamingView AddButton(string text, string callbackData) {
            _buttons.Add(new ViewButton(text, callbackData));
            return this;
        }

        public async Task<List<Model.Data.Message>> Render(CancellationToken cancellationToken = default) {
            if (_streamData == null) {
                return new List<Model.Data.Message>() { Model.Data.Message.FromTelegramMessage(await RenderFinalMessage(cancellationToken)) };
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

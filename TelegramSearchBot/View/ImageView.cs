using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.View {
    public class ImageView : IView {
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        // ViewModel properties
        private long _chatId;
        private int _replyToMessageId;
        private string _caption;
        private List<ViewButton> _buttons = new List<ViewButton>();
        private bool _disableNotification;
        private InputFile _photo;

        public ImageView(ITelegramBotClient botClient, SendMessage sendMessage) {
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
        public ImageView WithChatId(long chatId) {
            _chatId = chatId;
            return this;
        }

        public ImageView WithReplyTo(int messageId) {
            _replyToMessageId = messageId;
            return this;
        }

        public ImageView WithCaption(string caption) {
            _caption = MessageFormatHelper.ConvertMarkdownToTelegramHtml(caption);
            return this;
        }

        public ImageView DisableNotification(bool disable = true) {
            _disableNotification = disable;
            return this;
        }

        public ImageView AddButton(string text, string callbackData) {
            _buttons.Add(new ViewButton(text, callbackData));
            return this;
        }

        public ImageView WithPhoto(string dataUri) {
            var parts = dataUri.Split(new[] { ',' }, 2);
            if (parts.Length != 2)
                throw new ArgumentException("Invalid data URI format");

            var metaParts = parts[0].Split(new[] { ':', ';', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (metaParts.Length < 3 || metaParts[0] != "data" || metaParts[2] != "base64")
                throw new ArgumentException("Invalid data URI format");

            var mimeType = metaParts[1];
            var bytes = Convert.FromBase64String(parts[1]);
            _photo = InputFile.FromStream(new MemoryStream(bytes), $"photo.{mimeType.Split('/').Last()}");
            return this;
        }

        public ImageView WithPhotoBytes(byte[] imageBytes) {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("Image bytes cannot be null or empty");

            _photo = InputFile.FromStream(new MemoryStream(imageBytes), "photo.png");
            return this;
        }

        public async Task Render() {
            var replyParameters = new Telegram.Bot.Types.ReplyParameters {
                MessageId = _replyToMessageId
            };

            var inlineButtons = _buttons?.Select(b =>
                InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToList();

            var replyMarkup = inlineButtons != null && inlineButtons.Any() ?
                new InlineKeyboardMarkup(inlineButtons) : null;

            await _sendMessage.AddTaskWithResult(async () => await _botClient.SendPhoto(
                chatId: _chatId,
                photo: _photo,
                caption: _caption,
                parseMode: ParseMode.Html,
                replyParameters: replyParameters,
                disableNotification: _disableNotification,
                replyMarkup: replyMarkup
            ), _chatId);
        }
    }
}

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

namespace TelegramSearchBot.View
{
    public class VideoView : IView
    {
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        // ViewModel properties
        private long _chatId;
        private int _replyToMessageId;
        private string _caption;
        private List<ViewButton> _buttons = new List<ViewButton>();
        private bool _disableNotification;
        private InputFile _video;
        private int? _duration;
        private int? _width;
        private int? _height;
        private bool _supportsStreaming = true;
        private InputFile? _thumbnail;

        public VideoView(ITelegramBotClient botClient, SendMessage sendMessage) 
        {
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
        public VideoView WithChatId(long chatId)
        {
            _chatId = chatId;
            return this;
        }

        public VideoView WithReplyTo(int messageId)
        {
            _replyToMessageId = messageId;
            return this;
        }

        public VideoView WithCaption(string caption)
        {
            _caption = MessageFormatHelper.ConvertMarkdownToTelegramHtml(caption);
            return this;
        }

        public VideoView DisableNotification(bool disable = true)
        {
            _disableNotification = disable;
            return this;
        }

        public VideoView AddButton(string text, string callbackData)
        {
            _buttons.Add(new ViewButton(text, callbackData));
            return this;
        }

        public VideoView WithVideo(string dataUri)
        {
            var parts = dataUri.Split(new[] { ',' }, 2);
            if (parts.Length != 2)
                throw new ArgumentException("Invalid data URI format");

            var metaParts = parts[0].Split(new[] { ':', ';', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (metaParts.Length < 3 || metaParts[0] != "data" || metaParts[2] != "base64")
                throw new ArgumentException("Invalid data URI format");

            var mimeType = metaParts[1];
            var bytes = Convert.FromBase64String(parts[1]);
            _video = InputFile.FromStream(new MemoryStream(bytes), $"video.{mimeType.Split('/').Last()}");
            return this;
        }

        public VideoView WithVideoBytes(byte[] videoBytes)
        {
            if (videoBytes == null || videoBytes.Length == 0)
                throw new ArgumentException("Video bytes cannot be null or empty");
                
            _video = InputFile.FromStream(new MemoryStream(videoBytes), "video.mp4");
            return this;
        }

        public VideoView WithDuration(int duration)
        {
            _duration = duration;
            return this;
        }

        public VideoView WithDimensions(int width, int height)
        {
            _width = width;
            _height = height;
            return this;
        }

        public VideoView WithSupportsStreaming(bool supportsStreaming)
        {
            _supportsStreaming = supportsStreaming;
            return this;
        }

        public VideoView WithThumbnail(InputFile thumbnail)
        {
            _thumbnail = thumbnail;
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

            var replyMarkup = inlineButtons != null && inlineButtons.Any() ? 
                new InlineKeyboardMarkup(inlineButtons) : null;

            await _sendMessage.AddTaskWithResult(async () => await _botClient.SendVideo(
                chatId: _chatId,
                video: _video,
                caption: _caption,
                parseMode: ParseMode.Html,
                replyParameters: replyParameters,
                disableNotification: _disableNotification,
                duration: _duration,
                width: _width,
                height: _height,
                supportsStreaming: _supportsStreaming,
                thumbnail: _thumbnail,
                replyMarkup: replyMarkup
            ), _chatId);
        }
    }
}

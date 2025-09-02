using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Scriban;
using Telegram.Bot;
using Telegram.Bot.Extensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.View {
    public class VideoView : IView {
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<VideoView> _logger;

        // ViewModel properties
        private long _chatId;
        private int _replyToMessageId;
        private string _caption;
        private string _captionTemplate;
        private object _templateModel;

        private const string VideoCaptionTemplate = @"*{{ title }}*
UP: {{ owner_name }}
分类: {{ category }}
{{ original_url }}";

        private const string FallbackCaptionTemplate = @"*{{ title }}*
UP: {{ owner_name }}
分类: {{ category }}
{{ duration }}
{{ description }}
{{ original_url }}";
        private List<ViewButton> _buttons = new List<ViewButton>();
        private bool _disableNotification;
        private InputFile _video;
        private int? _duration;
        private int? _width;
        private int? _height;
        private bool _supportsStreaming = true;
        private InputFile? _thumbnail;

        public VideoView(ITelegramBotClient botClient, SendMessage sendMessage, ILogger<VideoView> logger) {
            _botClient = botClient;
            _sendMessage = sendMessage;
            _logger = logger;
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
        public VideoView WithChatId(long chatId) {
            _chatId = chatId;
            return this;
        }

        public VideoView WithReplyTo(int messageId) {
            _replyToMessageId = messageId;
            return this;
        }


        public VideoView WithTitle(string title) {
            if (_templateModel == null) _templateModel = new Dictionary<string, object>();
            ( ( Dictionary<string, object> ) _templateModel )["title"] = title;
            return this;
        }

        public VideoView WithOwnerName(string ownerName) {
            if (_templateModel == null) _templateModel = new Dictionary<string, object>();
            ( ( Dictionary<string, object> ) _templateModel )["owner_name"] = ownerName;
            return this;
        }

        public VideoView WithCategory(string category) {
            if (_templateModel == null) _templateModel = new Dictionary<string, object>();
            ( ( Dictionary<string, object> ) _templateModel )["category"] = category;
            return this;
        }

        public VideoView WithOriginalUrl(string originalUrl) {
            if (_templateModel == null) _templateModel = new Dictionary<string, object>();
            ( ( Dictionary<string, object> ) _templateModel )["original_url"] = originalUrl;
            return this;
        }

        public VideoView WithTemplateDuration(int duration) {
            if (_templateModel == null) _templateModel = new Dictionary<string, object>();
            ( ( Dictionary<string, object> ) _templateModel )["duration"] = duration > 0 ? $"时长: {TimeSpan.FromSeconds(duration):g}\n" : "";
            return this;
        }

        public VideoView WithTemplateDescription(string description) {
            if (_templateModel == null) _templateModel = new Dictionary<string, object>();
            ( ( Dictionary<string, object> ) _templateModel )["description"] = !string.IsNullOrWhiteSpace(description) ?
                $"简介: {description.Substring(0, Math.Min(description.Length, 100)) + ( description.Length > 100 ? "..." : "" )}\n" : "";
            return this;
        }

        public VideoView WithCaption(string caption) {
            _caption = MessageFormatHelper.ConvertMarkdownToTelegramHtml(caption);
            _captionTemplate = null; // Ensure template is not used if a direct caption is provided
            return this;
        }

        public VideoView DisableNotification(bool disable = true) {
            _disableNotification = disable;
            return this;
        }

        public VideoView AddButton(string text, string callbackData) {
            _buttons.Add(new ViewButton(text, callbackData));
            return this;
        }

        public VideoView WithVideo(InputFile videoInputFile) {
            _video = videoInputFile;
            return this;
        }

        public VideoView WithVideo(string dataUri) {
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

        public VideoView WithVideoBytes(byte[] videoBytes) {
            if (videoBytes == null || videoBytes.Length == 0)
                throw new ArgumentException("Video bytes cannot be null or empty");

            _video = InputFile.FromStream(new MemoryStream(videoBytes), "video.mp4");
            return this;
        }

        public VideoView WithDuration(int duration) {
            _duration = duration;
            return this;
        }

        public VideoView WithDimensions(int width, int height) {
            _width = width;
            _height = height;
            return this;
        }

        public VideoView WithSupportsStreaming(bool supportsStreaming) {
            _supportsStreaming = supportsStreaming;
            return this;
        }

        public VideoView WithThumbnail(InputFile thumbnail) {
            _thumbnail = thumbnail;
            return this;
        }

        public async Task<Message> Render() {
            var replyParameters = new Telegram.Bot.Types.ReplyParameters {
                MessageId = _replyToMessageId
            };

            var inlineButtons = _buttons?.Select(b =>
                InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToList();

            var replyMarkup = inlineButtons != null && inlineButtons.Any() ?
                new InlineKeyboardMarkup(inlineButtons) : null;

            if (_video == null) {
                // Handle text message case
                var caption = _caption ?? string.Empty;
                if (_captionTemplate == null && _templateModel != null) // Only use template if no direct caption is set
                {
                    try {
                        caption = Template.Parse(FallbackCaptionTemplate).Render(_templateModel);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error rendering fallback caption template");
                        caption = _caption ?? "视频内容";
                    }
                }

                await _sendMessage.AddTextMessageToSend(
                    chatId: _chatId,
                    text: caption,
                    parseMode: ParseMode.Html,
                    replyParameters: replyParameters,
                    disableNotification: _disableNotification,
                    highPriorityForGroup: _chatId < 0 // Groups have negative chat IDs
                );
                if (replyMarkup != null) {
                    await _sendMessage.AddTask(async () =>
                        await _botClient.SendMessage(
                            chatId: _chatId,
                            text: " ", // Empty message just to send buttons
                            replyMarkup: replyMarkup
                        ),
                        _chatId < 0
                    );
                }
                return new Message(); // Return dummy message since we don't have the actual message
            }

            // Handle video message case
            var videoCaption = _caption ?? string.Empty;
            if (_captionTemplate == null && _templateModel != null) // Only use template if no direct caption is set
            {
                try {
                    videoCaption = Template.Parse(VideoCaptionTemplate).Render(_templateModel);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error rendering video caption template");
                    videoCaption = _caption ?? "视频内容";
                }
            }

            return await _sendMessage.AddTaskWithResult(async () => await _botClient.SendVideo(
                chatId: _chatId,
                video: _video,
                caption: videoCaption,
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

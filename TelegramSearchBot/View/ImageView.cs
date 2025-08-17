using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
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
    public class ImageView : IView
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ISendMessageService _sendMessage;

        // ViewModel properties
        private long _chatId;
        private int _replyToMessageId;
        private string _caption;
        private List<ViewButton> _buttons = new List<ViewButton>();
        private bool _disableNotification;
        private InputFile _photo;

        public ImageView(ITelegramBotClient botClient, ISendMessageService sendMessage) 
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
            // ImageView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithCount(int count)
        {
            // ImageView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithSkip(int skip)
        {
            // ImageView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithTake(int take)
        {
            // ImageView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithSearchType(SearchType searchType)
        {
            // ImageView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithMessages(List<TelegramSearchBot.Model.Data.Message> messages)
        {
            // ImageView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithTitle(string title)
        {
            // ImageView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithHelp()
        {
            // ImageView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView DisableNotification(bool disable = true)
        {
            _disableNotification = disable;
            return this;
        }

        public IView WithMessage(string message)
        {
            // ImageView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithOwnerName(string ownerName)
        {
            // ImageView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public ImageView WithCaption(string caption)
        {
            _caption = MessageFormatHelper.ConvertMarkdownToTelegramHtml(caption);
            return this;
        }

        public ImageView AddButton(string text, string callbackData)
        {
            _buttons.Add(new ViewButton(text, callbackData));
            return this;
        }

        public ImageView WithPhoto(string dataUri)
        {
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

        public ImageView WithPhotoBytes(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("Image bytes cannot be null or empty");
                
            _photo = InputFile.FromStream(new MemoryStream(imageBytes), "photo.png");
            return this;
        }

        public async Task Render()
        {
            var inlineButtons = _buttons?.Select(b => 
                InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToList();

            var replyMarkup = inlineButtons != null && inlineButtons.Any() ? 
                new InlineKeyboardMarkup(inlineButtons) : null;

            // 简化实现：直接调用BotClient发送图片
            var replyParameters = _replyToMessageId > 0 ? new ReplyParameters { MessageId = _replyToMessageId } : null;
            
            await _botClient.SendPhoto(
                chatId: _chatId,
                photo: _photo,
                caption: _caption,
                parseMode: ParseMode.Html,
                replyParameters: replyParameters,
                disableNotification: _disableNotification,
                replyMarkup: replyMarkup
            );
        }
    }
}

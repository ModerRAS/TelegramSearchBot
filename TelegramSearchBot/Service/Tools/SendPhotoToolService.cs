using System;
using System.IO;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class SendPhotoToolService : IService, ISendPhotoToolService {
        public string ServiceName => "SendPhotoToolService";

        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        public SendPhotoToolService(ITelegramBotClient botClient, SendMessage sendMessage) {
            _botClient = botClient;
            _sendMessage = sendMessage;
        }

        [BuiltInTool("Sends a photo to the current chat using base64 encoded image data.", Name = "send_photo_base64")]
        public async Task<SendPhotoResult> SendPhotoBase64(
            [BuiltInParameter("The base64 encoded image data (without the data URI prefix).", IsRequired = true)] string base64Data,
            [BuiltInParameter("Optional caption for the photo (max 1024 characters).", IsRequired = false)] string caption,
            ToolContext toolContext,
            [BuiltInParameter("Optional message ID to reply to.", IsRequired = false)] int? replyToMessageId = null) {
            try {
                byte[] imageBytes;
                try {
                    imageBytes = Convert.FromBase64String(base64Data);
                } catch (FormatException) {
                    return new SendPhotoResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = "Invalid base64 data format. Please provide valid base64 encoded image data."
                    };
                }

                if (imageBytes.Length == 0) {
                    return new SendPhotoResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = "Image data is empty."
                    };
                }

                var photo = InputFile.FromStream(new MemoryStream(imageBytes), "photo.png");

                var replyParameters = replyToMessageId.HasValue
                    ? new ReplyParameters { MessageId = replyToMessageId.Value }
                    : null;

                var message = await _sendMessage.AddTaskWithResult(async () => await _botClient.SendPhoto(
                    chatId: toolContext.ChatId,
                    photo: photo,
                    caption: string.IsNullOrEmpty(caption) ? null : caption.Length > 1024 ? caption.Substring(0, 1024) : caption,
                    parseMode: ParseMode.Html,
                    replyParameters: replyParameters
                ), toolContext.ChatId);

                return new SendPhotoResult {
                    Success = true,
                    MessageId = message.MessageId,
                    ChatId = message.Chat.Id
                };
            } catch (Exception ex) {
                return new SendPhotoResult {
                    Success = false,
                    ChatId = toolContext.ChatId,
                    Error = $"Failed to send photo: {ex.Message}"
                };
            }
        }

        [BuiltInTool("Sends a photo to the current chat using a file path.", Name = "send_photo_file")]
        public async Task<SendPhotoResult> SendPhotoFile(
            [BuiltInParameter("The file path to the image on the server.", IsRequired = true)] string filePath,
            [BuiltInParameter("Optional caption for the photo (max 1024 characters).", IsRequired = false)] string caption,
            ToolContext toolContext,
            [BuiltInParameter("Optional message ID to reply to.", IsRequired = false)] int? replyToMessageId = null) {
            try {
                if (string.IsNullOrWhiteSpace(filePath)) {
                    return new SendPhotoResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = "File path cannot be empty."
                    };
                }

                if (!File.Exists(filePath)) {
                    return new SendPhotoResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = $"File not found: {filePath}"
                    };
                }

                var fileInfo = new FileInfo(filePath);
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var photo = InputFile.FromStream(fileStream, fileInfo.Name);

                var replyParameters = replyToMessageId.HasValue
                    ? new ReplyParameters { MessageId = replyToMessageId.Value }
                    : null;

                var message = await _sendMessage.AddTaskWithResult(async () => await _botClient.SendPhoto(
                    chatId: toolContext.ChatId,
                    photo: photo,
                    caption: string.IsNullOrEmpty(caption) ? null : caption.Length > 1024 ? caption.Substring(0, 1024) : caption,
                    parseMode: ParseMode.Html,
                    replyParameters: replyParameters
                ), toolContext.ChatId);

                return new SendPhotoResult {
                    Success = true,
                    MessageId = message.MessageId,
                    ChatId = message.Chat.Id
                };
            } catch (Exception ex) {
                return new SendPhotoResult {
                    Success = false,
                    ChatId = toolContext.ChatId,
                    Error = $"Failed to send photo: {ex.Message}"
                };
            }
        }
    }
}

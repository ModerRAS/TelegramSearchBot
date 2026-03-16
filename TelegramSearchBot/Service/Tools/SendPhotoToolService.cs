using System;
using System.IO;
using System.Threading.Tasks;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class SendPhotoToolService : IService, ISendPhotoToolService {
        public string ServiceName => "SendPhotoToolService";

        private readonly ISendMessageService _sendMessageService;

        public SendPhotoToolService(ISendMessageService sendMessageService) {
            _sendMessageService = sendMessageService;
        }

        [BuiltInTool("Sends a photo to the current chat using a base64-encoded image. The base64 string should include the data URL prefix (e.g., data:image/jpeg;base64,...) or just the raw base64 data.")]
        public async Task<ToolResult> SendPhotoBase64Async(
            [BuiltInParameter("The base64-encoded image data. Can include data URL prefix (data:image/jpeg;base64,...) or be raw base64 string.")] string base64,
            [BuiltInParameter("Optional caption text to include with the photo.", IsRequired = false)] string caption = null,
            ToolContext toolContext = null) {
            if (toolContext == null) {
                return new ToolResult { Success = false, Message = "Tool context is required." };
            }
            try {
                string cleanBase64 = base64;
                if (base64.Contains(",")) {
                    cleanBase64 = base64.Split(',')[1];
                }

                await _sendMessageService.SendPhotoAsyncBase64(
                    cleanBase64,
                    caption ?? string.Empty,
                    toolContext.ChatId,
                    0,
                    ParseMode.MarkdownV2
                );

                return new ToolResult { Success = true, Message = "Photo sent successfully." };
            } catch (Exception ex) {
                return new ToolResult { Success = false, Message = $"Failed to send photo: {ex.Message}" };
            }
        }

        [BuiltInTool("Sends a photo to the current chat using a file path. Supports common image formats (jpg, jpeg, png, gif, webp, bmp).")]
        public async Task<ToolResult> SendPhotoFileAsync(
            [BuiltInParameter("The file path to the image file on the local system.")] string filePath,
            [BuiltInParameter("Optional caption text to include with the photo.", IsRequired = false)] string caption = null,
            ToolContext toolContext = null) {
            if (toolContext == null) {
                return new ToolResult { Success = false, Message = "Tool context is required." };
            }
            try {
                if (!File.Exists(filePath)) {
                    return new ToolResult { Success = false, Message = $"File not found: {filePath}" };
                }

                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
                if (Array.IndexOf(supportedExtensions, extension) == -1) {
                    return new ToolResult { Success = false, Message = $"Unsupported image format: {extension}. Supported formats: {string.Join(", ", supportedExtensions)}" };
                }

                var fileName = Path.GetFileName(filePath);
                await _sendMessageService.SendPhotoAsync(
                    File.OpenRead(filePath),
                    caption ?? string.Empty,
                    fileName,
                    toolContext.ChatId,
                    0,
                    ParseMode.MarkdownV2
                );

                return new ToolResult { Success = true, Message = "Photo sent successfully." };
            } catch (Exception ex) {
                return new ToolResult { Success = false, Message = $"Failed to send photo: {ex.Message}" };
            }
        }
    }
}

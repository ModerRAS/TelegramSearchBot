using System;
using System.IO;
using System.Threading;
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
    public class SendVideoToolService : IService, ISendVideoToolService {
        public string ServiceName => "SendVideoToolService";

        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(120);

        public SendVideoToolService(ITelegramBotClient botClient, SendMessage sendMessage) {
            _botClient = botClient;
            _sendMessage = sendMessage;
        }

        /// <summary>
        /// Resolves the effective reply-to message ID. Uses the explicitly provided value if set,
        /// otherwise falls back to the ToolContext.MessageId (the original user message).
        /// </summary>
        private static ReplyParameters GetReplyParameters(long? explicitReplyToMessageId, ToolContext toolContext) {
            long? messageId = explicitReplyToMessageId ?? ( toolContext.MessageId != 0 ? toolContext.MessageId : ( long? ) null );
            return messageId.HasValue ? new ReplyParameters { MessageId = ( int ) messageId.Value } : null;
        }

        [BuiltInTool("Sends a video to the current chat using a file path.", Name = "send_video_file")]
        public async Task<SendVideoResult> SendVideoFile(
            [BuiltInParameter("The file path to the video on the server.")] string filePath,
            ToolContext toolContext,
            [BuiltInParameter("Optional caption for the video (max 1024 characters).", IsRequired = false)] string caption = null,
            [BuiltInParameter("Optional message ID to reply to.", IsRequired = false)] long? replyToMessageId = null) {
            try {
                if (string.IsNullOrWhiteSpace(filePath)) {
                    return new SendVideoResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = "File path cannot be empty."
                    };
                }

                if (!File.Exists(filePath)) {
                    return new SendVideoResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = $"File not found: {filePath}"
                    };
                }

                var fileInfo = new FileInfo(filePath);
                const long maxFileSizeBytes = 50 * 1024 * 1024; // Telegram video limit: 50 MB
                if (fileInfo.Length > maxFileSizeBytes) {
                    return new SendVideoResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = $"File is too large ({fileInfo.Length / 1024 / 1024}MB). Maximum allowed size is 50MB."
                    };
                }

                var fileBytes = await File.ReadAllBytesAsync(filePath);
                var video = InputFile.FromStream(new MemoryStream(fileBytes), fileInfo.Name);

                var replyParameters = GetReplyParameters(replyToMessageId, toolContext);

                using var cts = new CancellationTokenSource(SendTimeout);
                var message = await _sendMessage.AddTaskWithResult(async () => await _botClient.SendVideo(
                    chatId: toolContext.ChatId,
                    video: video,
                    caption: string.IsNullOrEmpty(caption) ? null : caption.Length > 1024 ? caption.Substring(0, 1024) : caption,
                    parseMode: ParseMode.Html,
                    replyParameters: replyParameters,
                    cancellationToken: cts.Token
                ), toolContext.ChatId);

                return new SendVideoResult {
                    Success = true,
                    MessageId = message.MessageId,
                    ChatId = message.Chat.Id
                };
            } catch (Exception ex) {
                return new SendVideoResult {
                    Success = false,
                    ChatId = toolContext.ChatId,
                    Error = $"Failed to send video: {ex.Message}"
                };
            }
        }
    }
}

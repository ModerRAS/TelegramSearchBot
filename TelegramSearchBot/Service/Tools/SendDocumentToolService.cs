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
    public class SendDocumentToolService : IService, ISendDocumentToolService {
        public string ServiceName => "SendDocumentToolService";

        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;

        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(120);

        public SendDocumentToolService(ITelegramBotClient botClient, SendMessage sendMessage) {
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

        [BuiltInTool("Sends a document/file to the current chat using a file path.", Name = "send_document_file")]
        public async Task<SendDocumentResult> SendDocumentFile(
            [BuiltInParameter("The file path to the document on the server.")] string filePath,
            ToolContext toolContext,
            [BuiltInParameter("Optional caption for the document (max 1024 characters).", IsRequired = false)] string caption = null,
            [BuiltInParameter("Optional message ID to reply to.", IsRequired = false)] long? replyToMessageId = null) {
            try {
                if (string.IsNullOrWhiteSpace(filePath)) {
                    return new SendDocumentResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = "File path cannot be empty."
                    };
                }

                if (!File.Exists(filePath)) {
                    return new SendDocumentResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = $"File not found: {filePath}"
                    };
                }

                var fileInfo = new FileInfo(filePath);
                const long maxFileSizeBytes = 2L * 1024 * 1024 * 1024;
                if (fileInfo.Length > maxFileSizeBytes) {
                    return new SendDocumentResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = $"File is too large ({fileInfo.Length / 1024 / 1024 / 1024}GB). Maximum allowed size is 2GB."
                    };
                }

                var fileBytes = await File.ReadAllBytesAsync(filePath);
                var document = InputFile.FromStream(new MemoryStream(fileBytes), fileInfo.Name);

                var replyParameters = GetReplyParameters(replyToMessageId, toolContext);

                using var cts = new CancellationTokenSource(SendTimeout);
                var message = await _sendMessage.AddTaskWithResult(async () => await _botClient.SendDocument(
                    chatId: toolContext.ChatId,
                    document: document,
                    caption: string.IsNullOrEmpty(caption) ? null : caption.Length > 1024 ? caption.Substring(0, 1024) : caption,
                    parseMode: ParseMode.Html,
                    replyParameters: replyParameters,
                    cancellationToken: cts.Token
                ), toolContext.ChatId);

                return new SendDocumentResult {
                    Success = true,
                    MessageId = message.MessageId,
                    ChatId = message.Chat.Id
                };
            } catch (Exception ex) {
                return new SendDocumentResult {
                    Success = false,
                    ChatId = toolContext.ChatId,
                    Error = $"Failed to send document: {ex.Message}"
                };
            }
        }
    }
}

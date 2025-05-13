using Orleans;
using Serilog;
using System;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types; // For ReplyParameters
using Telegram.Bot.Types.Enums; // For ParseMode if needed later
using TelegramSearchBot.Interfaces;

namespace TelegramSearchBot.Grains
{
    // Grain implementation
    public class TelegramMessageSenderGrain : Grain, ITelegramMessageSenderGrain
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger _logger;

        public TelegramMessageSenderGrain(ITelegramBotClient botClient)
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _logger = Log.ForContext<TelegramMessageSenderGrain>();
        }

        public async Task<int?> SendMessageAsync(TelegramMessageToSend message)
        {
            if (message == null)
            {
                _logger.Warning("SendMessageAsync called with null message.");
                return null;
            }

            try
            {
                _logger.Information("Attempting to send message to ChatId {ChatId}: {Text}", message.ChatId, message.Text); // Corrected: message.ChatId
                ReplyParameters replyParams = null;
                if (message.ReplyToMessageId.HasValue && message.ReplyToMessageId.Value != 0)
                {
                    replyParams = new ReplyParameters { MessageId = message.ReplyToMessageId.Value };
                }

                Message sentMessage;
                LinkPreviewOptions? linkPreviewOptionsToSend = message.DisableWebPagePreview.HasValue 
                    ? new LinkPreviewOptions { IsDisabled = message.DisableWebPagePreview.Value } 
                    : null;

                if (message.ParseMode.HasValue)
                {
                    // Call with ParseMode specified
                    sentMessage = await _botClient.SendTextMessageAsync(
                        chatId: message.ChatId,
                        text: message.Text,
                        parseMode: message.ParseMode.Value, // Pass the non-nullable value
                        linkPreviewOptions: linkPreviewOptionsToSend,
                        replyParameters: replyParams,
                        replyMarkup: message.ReplyMarkup
                        // Other optional parameters like messageThreadId, entities, disableNotification, protectContent will use their defaults
                    );
                }
                else
                {
                    // Call without ParseMode specified (rely on default behavior of the library for parseMode)
                    sentMessage = await _botClient.SendTextMessageAsync(
                        chatId: message.ChatId,
                        text: message.Text,
                        // parseMode parameter is omitted
                        linkPreviewOptions: linkPreviewOptionsToSend,
                        replyParameters: replyParams,
                        replyMarkup: message.ReplyMarkup
                        // Other optional parameters like messageThreadId, entities, disableNotification, protectContent will use their defaults
                    );
                }
                
                _logger.Information("Message successfully sent to ChatId {ChatId}, MessageId: {MessageId}", message.ChatId, sentMessage.MessageId);
                return sentMessage.MessageId;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending Telegram message to ChatId {ChatId}", message.ChatId); // Corrected: message.ChatId
                return null;
            }
        }

        public async Task DeleteMessageAsync(long chatId, int messageId)
        {
            if (messageId == 0)
            {
                _logger.Warning("DeleteMessageAsync called with invalid messageId (0) for ChatId {ChatId}.", chatId);
                return;
            }
            try
            {
                _logger.Information("Attempting to delete message {MessageId} from ChatId {ChatId}", messageId, chatId);
                await _botClient.DeleteMessageAsync(chatId, messageId);
                _logger.Information("Message {MessageId} successfully deleted from ChatId {ChatId}", messageId, chatId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting message {MessageId} from ChatId {ChatId}", messageId, chatId);
                // Optionally rethrow or handle specific Telegram API exceptions (e.g., message not found, not enough rights)
            }
        }

        public async Task<bool> EditMessageTextAsync(long chatId, int messageId, string newText, IReplyMarkup replyMarkup = null)
        {
            try
            {
                await _botClient.EditMessageTextAsync(
                    chatId: chatId,
                    messageId: messageId,
                    text: newText,
                    replyMarkup: replyMarkup
                );
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error editing message text for ChatId {ChatId}, MessageId {MessageId}", chatId, messageId);
                return false;
            }
        }

        public async Task<bool> EditMessageReplyMarkupAsync(long chatId, int messageId, IReplyMarkup replyMarkup)
        {
            try
            {
                await _botClient.EditMessageReplyMarkupAsync(
                    chatId: chatId,
                    messageId: messageId,
                    replyMarkup: replyMarkup
                );
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error editing message reply markup for ChatId {ChatId}, MessageId {MessageId}", chatId, messageId);
                return false;
            }
        }

        public async Task<int?> SendPhotoAsync(long chatId, byte[] photoBytes, string caption = null, int? replyToMessageId = null, IReplyMarkup replyMarkup = null)
        {
            try
            {
                using var stream = new System.IO.MemoryStream(photoBytes);
                var sent = await _botClient.SendPhotoAsync(
                    chatId: chatId,
                    photo: Telegram.Bot.Types.InputFiles.InputFile.FromStream(stream),
                    caption: caption,
                    replyToMessageId: replyToMessageId,
                    replyMarkup: replyMarkup
                );
                return sent.MessageId;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending photo to ChatId {ChatId}", chatId);
                return null;
            }
        }

        public async Task<int?> SendDocumentAsync(long chatId, byte[] fileBytes, string fileName, string caption = null, int? replyToMessageId = null, IReplyMarkup replyMarkup = null)
        {
            try
            {
                using var stream = new System.IO.MemoryStream(fileBytes);
                var sent = await _botClient.SendDocumentAsync(
                    chatId: chatId,
                    document: Telegram.Bot.Types.InputFiles.InputFile.FromStream(stream, fileName),
                    caption: caption,
                    replyToMessageId: replyToMessageId,
                    replyMarkup: replyMarkup
                );
                return sent.MessageId;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending document to ChatId {ChatId}", chatId);
                return null;
            }
        }

        public async Task<int?> SendVideoAsync(long chatId, byte[] videoBytes, string caption = null, int? replyToMessageId = null, IReplyMarkup replyMarkup = null)
        {
            try
            {
                using var stream = new System.IO.MemoryStream(videoBytes);
                var sent = await _botClient.SendVideoAsync(
                    chatId: chatId,
                    video: Telegram.Bot.Types.InputFiles.InputFile.FromStream(stream),
                    caption: caption,
                    replyToMessageId: replyToMessageId,
                    replyMarkup: replyMarkup
                );
                return sent.MessageId;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending video to ChatId {ChatId}", chatId);
                return null;
            }
        }
    }
}

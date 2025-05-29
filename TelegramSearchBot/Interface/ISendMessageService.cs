using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Interface
{
    public interface ISendMessageService : IService
    {
        #region Fallback Methods
        Task TrySendMessageWithFallback(
            long chatId,
            int messageId,
            string originalMarkdownText,
            ParseMode preferredParseMode,
            bool isGroup,
            int replyToMessageId,
            string initialContentForNewMessage,
            bool isEdit);

        Task AttemptFallbackSend(
            long chatId,
            int messageId,
            string originalMarkdownText,
            bool isGroup,
            int replyToMessageId,
            bool wasEditAttempt,
            string initialFailureReason);
        #endregion

        #region Standard Send Methods
        Task SendVideoAsync(InputFile video, string caption, long chatId, int replyTo, ParseMode parseMode = ParseMode.MarkdownV2);
        Task SendMediaGroupAsync(IEnumerable<IAlbumInputMedia> mediaGroup, long chatId, int replyTo);
        Task SendDocument(InputFile inputFile, long ChatId, int replyTo);
        Task SendDocument(Stream inputFile, string FileName, long ChatId, int replyTo);
        Task SendDocument(byte[] inputFile, string FileName, long ChatId, int replyTo);
        Task SendDocument(string inputFile, string FileName, long ChatId, int replyTo);
        Task SendMessage(string Text, Chat ChatId, int replyTo);
        Task SendMessage(string Text, long ChatId, int replyTo);
        Task SendMessage(string Text, long ChatId);
        Task SplitAndSendTextMessage(string Text, Chat ChatId, int replyTo);
        Task SplitAndSendTextMessage(string Text, long ChatId, int replyTo);
        #endregion

        #region Streaming Methods
        IAsyncEnumerable<Model.Data.Message> SendMessage(
            IAsyncEnumerable<string> messages,
            long ChatId,
            int replyTo,
            string InitialContent = "Initializing...",
            ParseMode parseMode = ParseMode.Html);

        Task<List<Model.Data.Message>> SendFullMessageStream(
            IAsyncEnumerable<string> fullMessagesStream,
            long chatId,
            int replyTo,
            string initialPlaceholderContent = "⏳",
            CancellationToken cancellationToken = default);

        Task<List<Model.Data.Message>> SendStreamingMessage(
            IAsyncEnumerable<string> messages,
            long chatId,
            int replyTo,
            string initialContent = "⏳",
            CancellationToken cancellationToken = default);
        #endregion
    }
} 
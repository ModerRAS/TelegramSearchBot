using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using Markdig;
using Telegram.Bot.Exceptions;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.Web;
using System.Threading;
using TelegramSearchBot.Helper;

namespace TelegramSearchBot.Service.BotAPI
{
    public class SendMessageService : IService
    {
        #region Fields and Constructor
        public string ServiceName => "SendMessageService";
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send; 
        private readonly ILogger<SendMessageService> logger;

        public SendMessageService(ITelegramBotClient botClient, SendMessage Send, ILogger<SendMessageService> logger)
        {
            this.Send = Send;
            this.botClient = botClient;
            this.logger = logger;
        }
        #endregion

        #region Standard Send Methods
        public async Task SendVideoAsync(InputFile video, string caption, long chatId, int replyTo, ParseMode parseMode = ParseMode.MarkdownV2)
        {
            await Send.AddTask(async () =>
            {
                await botClient.SendVideo(
                    chatId: chatId,
                    video: video,
                    caption: caption,
                    parseMode: parseMode,
                    replyParameters: new ReplyParameters() { MessageId = replyTo }
                );
            }, chatId < 0);
        }

        public async Task SendMediaGroupAsync(IEnumerable<IAlbumInputMedia> mediaGroup, long chatId, int replyTo)
        {
            await Send.AddTask(async () =>
            {
                await botClient.SendMediaGroup(
                    chatId: chatId,
                    media: mediaGroup,
                    replyParameters: new ReplyParameters() { MessageId = replyTo }
                );
            }, chatId < 0);
        }

        public async Task SendDocument(InputFile inputFile, long ChatId, int replyTo)
        {
            await Send.AddTask(async () =>
            {
                var message = await botClient.SendDocument(
                    chatId: ChatId,
                    document: inputFile,
                    replyParameters: new ReplyParameters() { MessageId = replyTo }
                    );
            }, ChatId < 0);
        }
        public Task SendDocument(Stream inputFile, string FileName, long ChatId, int replyTo) => SendDocument(InputFile.FromStream(inputFile, FileName), ChatId, replyTo);
        public Task SendDocument(byte[] inputFile, string FileName, long ChatId, int replyTo) => SendDocument(InputFile.FromStream(new MemoryStream(inputFile), FileName), ChatId, replyTo);
        public Task SendDocument(string inputFile, string FileName, long ChatId, int replyTo) => SendDocument(InputFile.FromStream(new MemoryStream(Encoding.UTF8.GetBytes(inputFile)), FileName), ChatId, replyTo);

        public Task SendMessage(string Text, Chat ChatId, int replyTo) => SendMessage(Text, ChatId.Id, replyTo);
        public async Task SendMessage(string Text, long ChatId, int replyTo)
        {
            await Send.AddTask(async () =>
            {
                await botClient.SendMessage(
                    chatId: ChatId,
                    disableNotification: true,
                    replyParameters: new ReplyParameters() { MessageId = replyTo },
                    text: Text
                    );
            }, ChatId < 0);
        }
        public async Task SendMessage(string Text, long ChatId)
        {
            await Send.AddTask(async () =>
            {
                await botClient.SendMessage(
                    chatId: ChatId,
                    disableNotification: true,
                    text: Text
                    );
            }, ChatId < 0);
        }
        #endregion

        #region Incremental Streaming Send Method (Old)
        public async IAsyncEnumerable<Model.Data.Message> SendMessage(IAsyncEnumerable<string> messages, long ChatId, int replyTo, string InitialContent = "Initializing...", ParseMode parseMode = ParseMode.Html)
        {
            var sentMessage = await botClient.SendMessage(
                chatId: ChatId,
                text: InitialContent,
                replyParameters: new ReplyParameters() { MessageId = replyTo }
            );
            StringBuilder builder = new StringBuilder();
            var tmpMessageId = sentMessage.MessageId;
            var datetime = DateTime.UtcNow;
            var messagesToYield = new List<Model.Data.Message>();

            try
            {
                await foreach (var PerMessage in messages)
                {
                    if (builder.Length > 1900)
                    {
                        tmpMessageId = sentMessage.MessageId;
                        var currentContent = builder.ToString();
                        messagesToYield.Add(new Model.Data.Message()
                        {
                            GroupId = ChatId, MessageId = sentMessage.MessageId, DateTime = sentMessage.Date,
                            ReplyToUserId = (await botClient.GetMe()).Id, ReplyToMessageId = tmpMessageId,
                            FromUserId = (await botClient.GetMe()).Id, Content = currentContent,
                        });
                        await this.TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, currentContent, parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
                        sentMessage = await botClient.SendMessage(
                            chatId: ChatId, text: InitialContent,
                            replyParameters: new ReplyParameters() { MessageId = tmpMessageId }
                        );
                        builder.Clear();
                    }
                    builder.Append(PerMessage);
                    if (DateTime.UtcNow - datetime > TimeSpan.FromSeconds(5))
                    {
                        datetime = DateTime.UtcNow;
                        await this.TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, builder.ToString(), parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
                    }
                }
                var finalContent = builder.ToString();
                await this.TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, finalContent, parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error sending streaming message to {ChatId}");
                if (builder.Length > 0)
                {
                    try
                    {
                        var plainTextContent = MessageFormatHelper.ConvertToPlainText(builder.ToString());
                        await botClient.SendMessage(
                            chatId: ChatId, text: "Message content could not be fully displayed due to an error. Partial content:\n" + plainTextContent,
                            replyParameters: new ReplyParameters() { MessageId = replyTo }
                        );
                    }
                    catch (Exception fallbackEx) { logger.LogError(fallbackEx, $"Error sending fallback plain text message to {ChatId}"); }
                }
            }

            messagesToYield.Add(new Model.Data.Message()
            {
                GroupId = ChatId, MessageId = sentMessage.MessageId, DateTime = sentMessage.Date,
                ReplyToUserId = (await botClient.GetMe()).Id, FromUserId = (await botClient.GetMe()).Id,
                ReplyToMessageId = tmpMessageId, Content = builder.ToString(),
            });

            foreach (var msg in messagesToYield) { yield return msg; }
        }
        #endregion

        #region Full Message Streaming (Simplified Throttling)
        public async Task<List<Model.Data.Message>> SendFullMessageStream(
            IAsyncEnumerable<string> fullMessagesStream,
            long chatId,
            int replyTo,
            string initialPlaceholderContent = "⏳", // Kept for signature, but not used to send a placeholder
            CancellationToken cancellationToken = default)
        {
            List<Message> sentTelegramMessages = new List<Message>();
            Dictionary<int, string> lastSentHtmlPerMessageId = new Dictionary<int, string>();
            DateTime lastApiSyncTime = DateTime.MinValue;
            TimeSpan syncInterval = TimeSpan.FromSeconds(1.0); 
            
            string latestMarkdownSnapshot = null; 
            string markdownActuallySynced = null; // Markdown content that was last passed to Synchronize
            List<string> chunksForDb = new List<string>(); 
            bool isFirstSync = true;

            try
            {
                await foreach (var markdownContent in fullMessagesStream.WithCancellation(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    latestMarkdownSnapshot = markdownContent ?? string.Empty; 

                    if (string.IsNullOrWhiteSpace(latestMarkdownSnapshot) && !sentTelegramMessages.Any()) 
                    {
                        logger.LogTrace("SendFullMessageStream: Stream provided initial whitespace/empty content and no messages exist; ignoring.");
                        continue;
                    }

                    var timeSinceLastSync = DateTime.UtcNow - lastApiSyncTime;
                    if (timeSinceLastSync < syncInterval && !isFirstSync)
                    {
                        logger.LogTrace("SendFullMessageStream: Throttling update, skipping intermediate content. Will process latest at end or next interval.");
                        continue; 
                    }
                    
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    lastApiSyncTime = DateTime.UtcNow;
                    isFirstSync = false;
                    markdownActuallySynced = latestMarkdownSnapshot; 
                    
                    chunksForDb = MessageFormatHelper.SplitMarkdownIntoChunks(markdownActuallySynced, 1900);
                    if (!chunksForDb.Any() && !string.IsNullOrEmpty(markdownActuallySynced))
                    {
                        chunksForDb.Add(string.Empty); 
                    }
                    
                    var syncResult = await SynchronizeTelegramMessagesInternalAsync(
                        chatId, replyTo, chunksForDb, sentTelegramMessages, lastSentHtmlPerMessageId, cancellationToken
                    );
                    sentTelegramMessages = syncResult.UpdatedMessages;
                    lastSentHtmlPerMessageId = syncResult.UpdatedHtmlMap;
                }
            }
            catch (OperationCanceledException) { logger.LogInformation("SendFullMessageStream: Stream consumption cancelled."); }
            catch (Exception ex) { logger.LogError(ex, "SendFullMessageStream: Error during stream consumption."); }
            
            // Final Synchronization: Process the very last snapshot if it wasn't the one just synced
            if (latestMarkdownSnapshot != null && latestMarkdownSnapshot != markdownActuallySynced)
            {
                logger.LogInformation("SendFullMessageStream: Performing final synchronization for the absolute latest content.");
                chunksForDb = MessageFormatHelper.SplitMarkdownIntoChunks(latestMarkdownSnapshot, 1900);
                if (!chunksForDb.Any() && !string.IsNullOrEmpty(latestMarkdownSnapshot)) chunksForDb.Add(string.Empty);
                
                var syncResult = await SynchronizeTelegramMessagesInternalAsync(
                       chatId, replyTo, chunksForDb, sentTelegramMessages, lastSentHtmlPerMessageId, CancellationToken.None); // Use CancellationToken.None for final flush
                sentTelegramMessages = syncResult.UpdatedMessages;
                // lastSentHtmlPerMessageId = syncResult.UpdatedHtmlMap; // Not strictly needed after this for DB log
            } else if (latestMarkdownSnapshot == null && sentTelegramMessages.Any()) {
                // If stream ended and last content was effectively null/empty, clear out existing messages
                logger.LogInformation("SendFullMessageStream: Stream ended with no content, clearing existing messages.");
                chunksForDb = new List<string> { string.Empty }; 
                 var syncResult = await SynchronizeTelegramMessagesInternalAsync(
                       chatId, replyTo, chunksForDb, sentTelegramMessages, lastSentHtmlPerMessageId, CancellationToken.None);
                sentTelegramMessages = syncResult.UpdatedMessages;
            }

            return await BuildResultForDbAsync(chatId, replyTo, sentTelegramMessages, chunksForDb, CancellationToken.None);
        }
        
        private async Task<(List<Message> UpdatedMessages, Dictionary<int, string> UpdatedHtmlMap)> SynchronizeTelegramMessagesInternalAsync(
            long chatId,
            int originalReplyTo,
            List<string> newMarkdownChunks,
            List<Message> currentTgMessages,
            Dictionary<int, string> currentHtmlMap,
            CancellationToken cancellationToken)
        {
            List<Message> nextTgMessagesState = new List<Message>();
            Dictionary<int, string> nextHtmlMap = new Dictionary<int, string>(currentHtmlMap);
            int effectiveReplyTo = originalReplyTo;

            for (int i = 0; i < newMarkdownChunks.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                string mdChunk = newMarkdownChunks[i];
                string htmlChunk = MessageFormatHelper.ConvertMarkdownToTelegramHtml(mdChunk);

                if (i == 0) effectiveReplyTo = originalReplyTo;
                else if (nextTgMessagesState.Any())
                {
                    var lastSentValidMsg = nextTgMessagesState.LastOrDefault(m => m != null && m.MessageId != 0);
                    if (lastSentValidMsg != null) effectiveReplyTo = lastSentValidMsg.MessageId;
                }

                if (i < currentTgMessages.Count)
                {
                    Message existingMsg = currentTgMessages[i];
                    if (existingMsg == null || existingMsg.MessageId == 0) {
                        this.logger.LogWarning($"SynchronizeMessages: Found null or invalid message in currentTgMessages at index {i}, skipping.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(htmlChunk))
                    {
                        bool contentIsSame = false;
                        if (nextHtmlMap.TryGetValue(existingMsg.MessageId, out string lastSentHtml))
                        {
                            if (lastSentHtml == htmlChunk) contentIsSame = true;
                        }

                        if (contentIsSame)
                        {
                            this.logger.LogInformation($"SynchronizeMessages: Message {existingMsg.MessageId} content is identical to new chunk {i}, skipping edit.");
                            nextTgMessagesState.Add(existingMsg); 
                        }
                        else
                        {
                            try
                            {
                                Message editedMsg = await this.botClient.EditMessageText(
                                    chatId: chatId, messageId: existingMsg.MessageId, text: htmlChunk,
                                    parseMode: ParseMode.Html, cancellationToken: cancellationToken);
                                if (editedMsg != null && editedMsg.MessageId != 0) {
                                    nextTgMessagesState.Add(editedMsg);
                                    nextHtmlMap[editedMsg.MessageId] = htmlChunk; 
                                } else {
                                    this.logger.LogWarning($"SynchronizeMessages: Editing TG message {existingMsg.MessageId} for chunk {i} returned null or invalid MessageId. Adding existing to list.");
                                    nextTgMessagesState.Add(existingMsg); 
                                }
                            }
                            catch (ApiRequestException apiEx) when (apiEx.ErrorCode == 400 && apiEx.Message.Contains("message is not modified"))
                            {
                                this.logger.LogInformation($"SynchronizeMessages: Message {existingMsg.MessageId} was not modified (API confirmation). Content identical.");
                                nextTgMessagesState.Add(existingMsg); 
                                nextHtmlMap[existingMsg.MessageId] = htmlChunk; 
                            }
                            catch (Exception ex) { 
                                this.logger.LogError(ex, $"SynchronizeMessages: Error editing TG message {existingMsg.MessageId} for chunk {i}. Adding existing to list."); 
                                nextTgMessagesState.Add(existingMsg); 
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            await this.botClient.DeleteMessage(chatId, existingMsg.MessageId, cancellationToken: cancellationToken);
                            this.logger.LogInformation($"SynchronizeMessages: Deleted message {existingMsg.MessageId} for empty chunk {i}.");
                            nextHtmlMap.Remove(existingMsg.MessageId);
                        }
                        catch (Exception ex) { this.logger.LogError(ex, $"SynchronizeMessages: Error deleting TG message {existingMsg.MessageId} for empty chunk {i}."); }
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(htmlChunk))
                    {
                        try
                        {
                            Message newMsg = await this.botClient.SendMessage(
                                chatId: chatId, text: htmlChunk, parseMode: ParseMode.Html,
                                replyParameters: new ReplyParameters { MessageId = effectiveReplyTo },
                                cancellationToken: cancellationToken);
                            if (newMsg != null && newMsg.MessageId != 0) {
                                nextTgMessagesState.Add(newMsg);
                                nextHtmlMap[newMsg.MessageId] = htmlChunk;
                            } else {
                                this.logger.LogWarning($"SynchronizeMessages: Sending new TG message for chunk {i} returned null or invalid Message object.");
                            }
                        }
                        catch (Exception ex) { this.logger.LogError(ex, $"SynchronizeMessages: Error sending new TG message for chunk {i}."); }
                    }
                }
            }

            for (int i = newMarkdownChunks.Count; i < currentTgMessages.Count; i++)
            {
                if (currentTgMessages[i] == null || currentTgMessages[i].MessageId == 0) continue;
                try
                {
                    await this.botClient.DeleteMessage(chatId, currentTgMessages[i].MessageId, cancellationToken: cancellationToken);
                    this.logger.LogInformation($"SynchronizeMessages: Deleted superfluous message {currentTgMessages[i].MessageId}.");
                    nextHtmlMap.Remove(currentTgMessages[i].MessageId);
                }
                catch (Exception ex) { this.logger.LogError(ex, $"SynchronizeMessages: Error deleting superfluous TG message {currentTgMessages[i].MessageId}."); }
            }
            return (nextTgMessagesState, nextHtmlMap);
        }

        private async Task<List<Model.Data.Message>> BuildResultForDbAsync(
            long chatId, 
            int originalReplyTo, 
            List<Message> finalSentTgMessages, 
            List<string> finalMarkdownChunks, 
            CancellationToken cancellationToken)
        {
            var resultMessagesForDb = new List<Model.Data.Message>();
            User botUser = null;

            for (int i = 0; i < finalSentTgMessages.Count; i++)
            {
                var tgMsg = finalSentTgMessages[i];
                if (tgMsg == null || tgMsg.MessageId == 0) continue;

                if (botUser == null)
                {
                    try { 
                        botUser = await this.botClient.GetMe(cancellationToken: cancellationToken); 
                    }
                    catch (OperationCanceledException) { this.logger.LogInformation("BuildResultForDbAsync: GetMe cancelled."); break; } 
                    catch (Exception ex) { this.logger.LogError(ex, "BuildResultForDbAsync: Failed to get bot user info."); }
                }

                int msgReplyToId;
                if (i == 0) msgReplyToId = originalReplyTo;
                else if (i > 0 && finalSentTgMessages[i - 1] != null && finalSentTgMessages[i - 1].MessageId != 0) 
                    msgReplyToId = finalSentTgMessages[i - 1].MessageId;
                else msgReplyToId = originalReplyTo;

                resultMessagesForDb.Add(new Model.Data.Message()
                {
                    GroupId = chatId,
                    MessageId = tgMsg.MessageId,
                    DateTime = tgMsg.Date.ToUniversalTime(),
                    Content = (i < finalMarkdownChunks.Count) ? finalMarkdownChunks[i] : "",
                    FromUserId = botUser?.Id ?? 0,
                    ReplyToMessageId = msgReplyToId,
                });
            }
            return resultMessagesForDb;
        }
        #endregion

        #region Fallback and Formatting Helpers
        private async Task TrySendMessageWithFallback(long chatId, int messageId, string originalMarkdownText, ParseMode preferredParseMode, bool isGroup, int replyToMessageId, string initialContentForNewMessage, bool isEdit)
        {
            string textToSend = originalMarkdownText;
            ParseMode currentParseMode = preferredParseMode;

            if (preferredParseMode == ParseMode.Html)
            {
                textToSend = MessageFormatHelper.ConvertMarkdownToTelegramHtml(originalMarkdownText);
            }
            
            try
            {
                if (isEdit)
                {
                    Message editedMessage = null;
                    await Send.AddTask(async () =>
                    {
                        editedMessage = await botClient.EditMessageText(
                            chatId: chatId, messageId: messageId, parseMode: currentParseMode, text: textToSend);
                    }, isGroup);

                    if (editedMessage != null && editedMessage.MessageId > 0)
                    { logger.LogInformation($"Edited message {editedMessage.MessageId} successfully with {currentParseMode}."); }
                    else 
                    { logger.LogWarning($"Editing message {messageId} with {currentParseMode} failed silently. Edit will be skipped.");}
                }
                else 
                {
                    Message sentMsg = null;
                    await Send.AddTask(async () =>
                    {
                        sentMsg = await botClient.SendMessage(
                            chatId: chatId, text: textToSend, parseMode: currentParseMode, 
                            replyParameters: new ReplyParameters() { MessageId = replyToMessageId });
                    }, isGroup);

                    if (sentMsg != null && sentMsg.MessageId > 0)
                    { logger.LogInformation($"Sent new message {sentMsg.MessageId} successfully with {currentParseMode}."); }
                    else 
                    { 
                        logger.LogWarning($"Sending new message to {chatId} with {currentParseMode} failed silently. Attempting fallback.");
                        await AttemptFallbackSend(chatId, messageId, originalMarkdownText, isGroup, replyToMessageId, false, $"{currentParseMode} send failed silently");
                    }
                }
            }
            catch (ApiRequestException apiEx) when (apiEx.Message.Contains("can't parse entities") || apiEx.Message.Contains("unclosed tag") || apiEx.ErrorCode == 400)
            {
                if (isEdit)
                { logger.LogWarning(apiEx, $"Failed to edit message {messageId} with {currentParseMode} due to API error. Edit will be skipped."); }
                else 
                { 
                    logger.LogWarning(apiEx, $"Failed to send new message to {chatId} with {currentParseMode} due to API error. Attempting fallback.");
                    await AttemptFallbackSend(chatId, messageId, originalMarkdownText, isGroup, replyToMessageId, false, apiEx.Message);
                }
            }
            catch (Exception ex)
            {
                if (isEdit)
                { logger.LogError(ex, $"An unexpected error occurred while editing message {messageId}. Edit will be skipped.");}
                else 
                { 
                    logger.LogError(ex, $"An unexpected error occurred while sending new message to {chatId}. Attempting fallback.");
                    await AttemptFallbackSend(chatId, messageId, originalMarkdownText, isGroup, replyToMessageId, false, $"Unexpected error: {ex.Message}");
                }
            }
        }
        
        private async Task AttemptFallbackSend(long chatId, int messageId, string originalMarkdownText, bool isGroup, int replyToMessageId, bool wasEditAttempt, string initialFailureReason)
        {
            var plainText = MessageFormatHelper.ConvertToPlainText(originalMarkdownText);
            try
            {
                if (wasEditAttempt) 
                {
                    await Send.AddTask(async () => {
                        var fallbackEditedMessage = await botClient.EditMessageText(chatId: chatId, messageId: messageId, text: plainText);
                        logger.LogInformation($"Successfully resent message {fallbackEditedMessage.MessageId} as plain text after Markdown failure ({initialFailureReason}).");
                    }, isGroup);
                }
                else 
                {
                    await Send.AddTask(async () => {
                        var fallbackSentMsg = await botClient.SendMessage(chatId: chatId, text: plainText, replyParameters: new ReplyParameters() { MessageId = replyToMessageId });
                        logger.LogInformation($"Successfully sent new message {fallbackSentMsg.MessageId} as plain text after Markdown failure ({initialFailureReason}).");
                    }, isGroup);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to send message to {chatId} even as plain text. Initial failure: {initialFailureReason}.");
                if (!wasEditAttempt) { 
                    await Send.AddTask(async () => {
                        await botClient.SendMessage(chatId: chatId, text: "An error occurred while formatting the message.", replyParameters: new ReplyParameters() { MessageId = replyToMessageId });
                    }, isGroup);
                }
            }
        }
        #endregion
    }
}

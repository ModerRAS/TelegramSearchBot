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
                        var plainTextContent = ConvertToPlainText(builder.ToString());
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
                    
                    chunksForDb = SplitMarkdownIntoChunks(markdownActuallySynced, 1900);
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
                chunksForDb = SplitMarkdownIntoChunks(latestMarkdownSnapshot, 1900);
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
                string htmlChunk = this.ConvertMarkdownToTelegramHtml(mdChunk);

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
                textToSend = ConvertMarkdownToTelegramHtml(originalMarkdownText);
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
            var plainText = ConvertToPlainText(originalMarkdownText);
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

        private string ConvertMarkdownToTelegramHtml(string markdownText)
        {
            if (string.IsNullOrEmpty(markdownText)) return string.Empty;
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UsePipeTables().Build();
            string rawHtml = Markdig.Markdown.ToHtml(markdownText, pipeline);
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);
            StringBuilder telegramHtmlBuilder = new StringBuilder();
            ProcessHtmlNode(doc.DocumentNode, telegramHtmlBuilder);
            return telegramHtmlBuilder.ToString().Trim();
        }

        private void ProcessHtmlNode(HtmlNode node, StringBuilder builder)
        {
            switch (node.NodeType)
            {
                case HtmlNodeType.Element:
                    string tagName = node.Name.ToLowerInvariant();
                    switch (tagName)
                    {
                        case "b": case "strong": builder.Append("<b>"); ProcessChildren(node, builder); builder.Append("</b>"); break;
                        case "i": case "em": builder.Append("<i>"); ProcessChildren(node, builder); builder.Append("</i>"); break;
                        case "u": builder.Append("<u>"); ProcessChildren(node, builder); builder.Append("</u>"); break;
                        case "s": case "strike": case "del": builder.Append("<s>"); ProcessChildren(node, builder); builder.Append("</s>"); break;
                        case "tg-spoiler": builder.Append("<tg-spoiler>"); ProcessChildren(node, builder); builder.Append("</tg-spoiler>"); break;
                        case "a":
                            string href = node.GetAttributeValue("href", null);
                            if (!string.IsNullOrEmpty(href)) { builder.Append($"<a href=\"{HttpUtility.HtmlEncode(href)}\">"); ProcessChildren(node, builder); builder.Append("</a>"); }
                            else { ProcessChildren(node, builder); }
                            break;
                        case "code":
                            if (node.ParentNode != null && node.ParentNode.Name.ToLowerInvariant() == "pre")
                            {
                                string langClass = node.GetAttributeValue("class", "");
                                if (!string.IsNullOrEmpty(langClass) && langClass.StartsWith("language-")) builder.Append($"<code class=\"{HttpUtility.HtmlEncode(langClass)}\">");
                                else builder.Append("<code>");
                                builder.Append(HttpUtility.HtmlEncode(node.InnerText)); 
                                builder.Append("</code>");
                            }
                            else { builder.Append("<code>"); builder.Append(HttpUtility.HtmlEncode(node.InnerText)); builder.Append("</code>"); }
                            break;
                        case "pre":
                            builder.Append("<pre>");
                            if (node.ChildNodes.Count == 1 && node.FirstChild.Name.ToLowerInvariant() == "code") ProcessHtmlNode(node.FirstChild, builder);
                            else builder.Append(HttpUtility.HtmlEncode(node.InnerText)); 
                            builder.Append("</pre>");
                            break;
                        case "table": builder.Append(FormatHtmlTableAsPreformattedText(node)); break;
                        case "p": ProcessChildren(node, builder); builder.Append("\n"); break;
                        case "br": builder.Append("\n"); break;
                        case "hr": builder.Append("\n――――――――――――\n"); break;
                        case "h1": case "h2": case "h3": case "h4": case "h5": case "h6": builder.Append("<b>"); ProcessChildren(node, builder); builder.Append("</b>\n"); break;
                        case "ul": case "ol": ProcessList(node, builder, tagName == "ol" ? 1 : 0); builder.Append("\n"); break;
                        case "blockquote":
                            var blockquoteContent = new StringBuilder(); ProcessChildren(node, blockquoteContent);
                            var lines = blockquoteContent.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines) builder.Append($"> {line}\n");
                            break;
                        case "span": case "div": case "font": 
                        case "img": 
                            if (tagName == "img")
                            {
                                string alt = node.GetAttributeValue("alt", null); string src = node.GetAttributeValue("src", null);
                                if (!string.IsNullOrEmpty(alt)) builder.Append($"[Image: {HttpUtility.HtmlEncode(alt)}] ");
                                else if (!string.IsNullOrEmpty(src)) builder.Append($"[Image: {HttpUtility.HtmlEncode(src)}] ");
                            }
                            ProcessChildren(node, builder); break;
                        default: ProcessChildren(node, builder); break;
                    }
                    break;
                case HtmlNodeType.Text: builder.Append(HttpUtility.HtmlEncode(HttpUtility.HtmlDecode(node.InnerText))); break;
                case HtmlNodeType.Document: ProcessChildren(node, builder); break;
            }
        }

        private void ProcessChildren(HtmlNode parentNode, StringBuilder builder)
        {
            foreach (var childNode in parentNode.ChildNodes) ProcessHtmlNode(childNode, builder);
        }
        
        private void ProcessList(HtmlNode listNode, StringBuilder builder, int startNumber)
        {
            var items = listNode.SelectNodes("./li");
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                if (startNumber > 0) builder.Append($"{startNumber + i}. ");
                else builder.Append("• ");
                var liContent = new StringBuilder(); ProcessChildren(items[i], liContent);
                builder.Append(liContent.ToString().Trim()); 
                builder.Append("\n");
            }
        }

        private string FormatHtmlTableAsPreformattedText(HtmlNode tableNode)
        {
            var rows = new List<List<string>>(); var columnWidths = new List<int>();
            foreach (var rowNode in tableNode.SelectNodes(".//tr"))
            {
                var cells = new List<string>(); int currentCellIndex = 0;
                foreach (var cellNode in rowNode.SelectNodes(".//th|.//td"))
                {
                    string cellText = HttpUtility.HtmlDecode(cellNode.InnerText.Trim()); cells.Add(cellText);
                    if (columnWidths.Count <= currentCellIndex) columnWidths.Add(cellText.Length);
                    else columnWidths[currentCellIndex] = Math.Max(columnWidths[currentCellIndex], cellText.Length);
                    currentCellIndex++;
                }
                rows.Add(cells);
            }
            if (!rows.Any()) return "";
            StringBuilder tableBuilder = new StringBuilder(); tableBuilder.Append("<pre>");
            bool hasHeader = tableNode.SelectSingleNode(".//th") != null;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                for (int j = 0; j < row.Count; j++)
                {
                    tableBuilder.Append(row[j].PadRight(columnWidths[j]));
                    if (j < row.Count - 1) tableBuilder.Append(" | ");
                }
                tableBuilder.Append("\n");
                if (hasHeader && i == 0 && rows.Count > 1)
                {
                    for (int j = 0; j < columnWidths.Count; j++)
                    {
                        tableBuilder.Append(new string('-', columnWidths[j]));
                        if (j < columnWidths.Count - 1) tableBuilder.Append("-+-");
                    }
                    tableBuilder.Append("\n");
                }
            }
            tableBuilder.Append("</pre>"); return tableBuilder.ToString();
        }

        private string ConvertToPlainText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            string htmlText = Markdig.Markdown.ToHtml(text, pipeline);
            HtmlDocument doc = new HtmlDocument(); doc.LoadHtml(htmlText);
            doc.DocumentNode.Descendants().Where(n => n.Name == "script" || n.Name == "style").ToList().ForEach(n => n.Remove());
            StringBuilder plainTextBuilder = new StringBuilder();
            foreach (HtmlNode node in doc.DocumentNode.DescendantsAndSelf())
            {
                if (node.NodeType == HtmlNodeType.Text) { if (node.ParentNode.Name != "script" && node.ParentNode.Name != "style") plainTextBuilder.Append(HttpUtility.HtmlDecode(node.InnerText)); }
                else if (node.Name == "br" || node.Name == "p" || node.Name == "div") plainTextBuilder.Append(" ");
            }
            string result = Regex.Replace(plainTextBuilder.ToString(), @"\s+", " ").Trim();
            result = Regex.Replace(result, @" ([\r\n])", "$1"); 
            result = Regex.Replace(result, @"([\r\n]) ", "$1"); 
            result = Regex.Replace(result, @"([\r\n]){2,}", "\n\n"); 
            return result.Trim();
        }

        private List<string> SplitMarkdownIntoChunks(string markdown, int maxLength)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(markdown)) return chunks;
            if (maxLength <= 0) { chunks.Add(markdown); return chunks; }
            int currentPosition = 0;
            while (currentPosition < markdown.Length)
            {
                int lengthToTake = Math.Min(maxLength, markdown.Length - currentPosition);
                string chunk;
                if (markdown.Length - currentPosition <= maxLength) { chunk = markdown.Substring(currentPosition); lengthToTake = chunk.Length; }
                else
                {
                    int splitPoint = -1;
                    for (int i = lengthToTake - 2; i > maxLength / 3; i--) 
                    { if (markdown[currentPosition + i] == '\n' && markdown[currentPosition + i + 1] == '\n') { splitPoint = i + 2; break; } }
                    if (splitPoint == -1) { for (int i = lengthToTake - 1; i > maxLength / 3; i--) { if (markdown[currentPosition + i] == '\n') { splitPoint = i + 1; break; } } }
                    if (splitPoint != -1) lengthToTake = splitPoint;
                    chunk = markdown.Substring(currentPosition, lengthToTake);
                }
                chunks.Add(chunk); currentPosition += lengthToTake;
            }
            return chunks;
        }
        #endregion
    }
}

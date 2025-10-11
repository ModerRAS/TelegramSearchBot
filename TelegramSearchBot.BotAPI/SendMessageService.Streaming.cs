using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service.BotAPI {
    /// <summary>
    /// 提供Telegram消息流式发送功能的服务类
    /// </summary>
    public partial class SendMessageService {
        #region Incremental Streaming Send Method (Old)
        /// <summary>
        /// 增量式流式发送消息(旧版实现)
        /// </summary>
        /// <param name="messages">异步消息流</param>
        /// <param name="ChatId">目标聊天ID</param>
        /// <param name="replyTo">回复的消息ID</param>
        /// <param name="InitialContent">初始占位内容</param>
        /// <param name="parseMode">消息解析模式</param>
        /// <returns>异步枚举的消息集合</returns>
        public async IAsyncEnumerable<Model.Data.Message> SendMessage(IAsyncEnumerable<string> messages, long ChatId, int replyTo, string InitialContent = "Initializing...", ParseMode parseMode = ParseMode.Html) {
            var sentMessage = await botClient.SendMessage(
                chatId: ChatId,
                text: InitialContent,
                replyParameters: new ReplyParameters() { MessageId = replyTo }
            );
            StringBuilder builder = new StringBuilder();
            var tmpMessageId = sentMessage.MessageId;
            var datetime = DateTime.UtcNow;
            var messagesToYield = new List<Model.Data.Message>();

            try {
                await foreach (var PerMessage in messages) {
                    if (builder.Length > 1900) {
                        tmpMessageId = sentMessage.MessageId;
                        var currentContent = builder.ToString();
                        messagesToYield.Add(new Model.Data.Message() {
                            GroupId = ChatId,
                            MessageId = sentMessage.MessageId,
                            DateTime = sentMessage.Date,
                            ReplyToUserId = ( await botClient.GetMe() ).Id,
                            ReplyToMessageId = tmpMessageId,
                            FromUserId = ( await botClient.GetMe() ).Id,
                            Content = currentContent,
                        });
                        await this.TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, currentContent, parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
                        sentMessage = await botClient.SendMessage(
                            chatId: ChatId, text: InitialContent,
                            replyParameters: new ReplyParameters() { MessageId = tmpMessageId }
                        );
                        builder.Clear();
                    }
                    builder.Append(PerMessage);
                    if (DateTime.UtcNow - datetime > TimeSpan.FromSeconds(5)) {
                        datetime = DateTime.UtcNow;
                        await this.TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, builder.ToString(), parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
                    }
                }
                var finalContent = builder.ToString();
                await this.TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, finalContent, parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
            } catch (Exception ex) {
                logger.LogError(ex, $"Error sending streaming message to {ChatId}");
                if (builder.Length > 0) {
                    try {
                        var plainTextContent = MessageFormatHelper.ConvertToPlainText(builder.ToString());
                        await botClient.SendMessage(
                            chatId: ChatId, text: "Message content could not be fully displayed due to an error. Partial content:\n" + plainTextContent,
                            replyParameters: new ReplyParameters() { MessageId = replyTo }
                        );
                    } catch (Exception fallbackEx) { logger.LogError(fallbackEx, $"Error sending fallback plain text message to {ChatId}"); }
                }
            }

            messagesToYield.Add(new Model.Data.Message() {
                GroupId = ChatId,
                MessageId = sentMessage.MessageId,
                DateTime = sentMessage.Date,
                ReplyToUserId = ( await botClient.GetMe() ).Id,
                FromUserId = ( await botClient.GetMe() ).Id,
                ReplyToMessageId = tmpMessageId,
                Content = builder.ToString(),
            });

            foreach (var msg in messagesToYield) { yield return msg; }
        }
        #endregion

        #region Full Message Streaming (Simplified Throttling)
        /// <summary>
        /// 完整消息流式发送(带简化节流控制)
        /// </summary>
        /// <param name="fullMessagesStream">完整消息流</param>
        /// <param name="chatId">目标聊天ID</param>
        /// <param name="replyTo">回复的消息ID</param>
        /// <param name="initialPlaceholderContent">初始占位内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送成功的消息列表</returns>
        public async Task<List<Model.Data.Message>> SendFullMessageStream(
            IAsyncEnumerable<string> fullMessagesStream,
            long chatId,
            int replyTo,
            string initialPlaceholderContent = "⏳",
            CancellationToken cancellationToken = default) {
            List<Message> sentTelegramMessages = new List<Message>();
            Dictionary<int, string> lastSentHtmlPerMessageId = new Dictionary<int, string>();
            DateTime lastApiSyncTime = DateTime.MinValue;
            TimeSpan syncInterval = TimeSpan.FromSeconds(1.0);

            string latestMarkdownSnapshot = null;
            string markdownActuallySynced = null;
            List<string> chunksForDb = new List<string>();
            bool isFirstSync = true;

            try {
                await foreach (var markdownContent in fullMessagesStream.WithCancellation(cancellationToken)) {
                    if (cancellationToken.IsCancellationRequested) break;

                    latestMarkdownSnapshot = markdownContent ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(latestMarkdownSnapshot) && !sentTelegramMessages.Any()) {
                        logger.LogTrace("SendFullMessageStream: Stream provided initial whitespace/empty content and no messages exist; ignoring.");
                        continue;
                    }

                    var timeSinceLastSync = DateTime.UtcNow - lastApiSyncTime;
                    if (timeSinceLastSync < syncInterval && !isFirstSync) {
                        logger.LogTrace("SendFullMessageStream: Throttling update, skipping intermediate content. Will process latest at end or next interval.");
                        continue;
                    }

                    if (cancellationToken.IsCancellationRequested) break;

                    lastApiSyncTime = DateTime.UtcNow;
                    isFirstSync = false;
                    markdownActuallySynced = latestMarkdownSnapshot;

                    chunksForDb = MessageFormatHelper.SplitMarkdownIntoChunks(markdownActuallySynced, 1900);
                    if (!chunksForDb.Any() && !string.IsNullOrEmpty(markdownActuallySynced)) {
                        chunksForDb.Add(string.Empty);
                    }

                    var syncResult = await SynchronizeTelegramMessagesInternalAsync(
                        chatId, replyTo, chunksForDb, sentTelegramMessages, lastSentHtmlPerMessageId, cancellationToken
                    );
                    sentTelegramMessages = syncResult.UpdatedMessages;
                    lastSentHtmlPerMessageId = syncResult.UpdatedHtmlMap;
                }
            } catch (OperationCanceledException) { logger.LogInformation("SendFullMessageStream: Stream consumption cancelled."); } catch (Exception ex) { logger.LogError(ex, "SendFullMessageStream: Error during stream consumption."); }

            if (latestMarkdownSnapshot != null && latestMarkdownSnapshot != markdownActuallySynced) {
                logger.LogInformation("SendFullMessageStream: Performing final synchronization for the absolute latest content.");
                chunksForDb = MessageFormatHelper.SplitMarkdownIntoChunks(latestMarkdownSnapshot, 1900);
                if (!chunksForDb.Any() && !string.IsNullOrEmpty(latestMarkdownSnapshot)) chunksForDb.Add(string.Empty);

                var syncResult = await SynchronizeTelegramMessagesInternalAsync(
                       chatId, replyTo, chunksForDb, sentTelegramMessages, lastSentHtmlPerMessageId, CancellationToken.None);
                sentTelegramMessages = syncResult.UpdatedMessages;
            } else if (latestMarkdownSnapshot == null && sentTelegramMessages.Any()) {
                logger.LogInformation("SendFullMessageStream: Stream ended with no content, clearing existing messages.");
                chunksForDb = new List<string> { string.Empty };
                var syncResult = await SynchronizeTelegramMessagesInternalAsync(
                      chatId, replyTo, chunksForDb, sentTelegramMessages, lastSentHtmlPerMessageId, CancellationToken.None);
                sentTelegramMessages = syncResult.UpdatedMessages;
            }

            return await BuildResultForDbAsync(chatId, replyTo, sentTelegramMessages, chunksForDb, CancellationToken.None);
        }

        /// <summary>
        /// 内部方法：同步Telegram消息状态
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="originalReplyTo">原始回复消息ID</param>
        /// <param name="newMarkdownChunks">新的Markdown消息块</param>
        /// <param name="currentTgMessages">当前Telegram消息列表</param>
        /// <param name="currentHtmlMap">当前HTML内容映射</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>更新后的消息列表和HTML映射</returns>
        private async Task<(List<Message> UpdatedMessages, Dictionary<int, string> UpdatedHtmlMap)> SynchronizeTelegramMessagesInternalAsync(
            long chatId,
            int originalReplyTo,
            List<string> newMarkdownChunks,
            List<Message> currentTgMessages,
            Dictionary<int, string> currentHtmlMap,
            CancellationToken cancellationToken) {
            List<Message> nextTgMessagesState = new List<Message>();
            Dictionary<int, string> nextHtmlMap = new Dictionary<int, string>(currentHtmlMap);
            int effectiveReplyTo = originalReplyTo;

            for (int i = 0; i < newMarkdownChunks.Count; i++) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                string mdChunk = newMarkdownChunks[i];
                string htmlChunk = MessageFormatHelper.ConvertMarkdownToTelegramHtml(mdChunk);

                if (i == 0) effectiveReplyTo = originalReplyTo;
                else if (nextTgMessagesState.Any()) {
                    var lastSentValidMsg = nextTgMessagesState.LastOrDefault(m => m != null && m.MessageId != 0);
                    if (lastSentValidMsg != null) effectiveReplyTo = lastSentValidMsg.MessageId;
                }

                if (i < currentTgMessages.Count) {
                    Message existingMsg = currentTgMessages[i];
                    if (existingMsg == null || existingMsg.MessageId == 0) {
                        this.logger.LogWarning($"SynchronizeMessages: Found null or invalid message in currentTgMessages at index {i}, skipping.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(htmlChunk)) {
                        bool contentIsSame = false;
                        if (nextHtmlMap.TryGetValue(existingMsg.MessageId, out string lastSentHtml)) {
                            if (lastSentHtml == htmlChunk) contentIsSame = true;
                        }

                        if (contentIsSame) {
                            this.logger.LogInformation($"SynchronizeMessages: Message {existingMsg.MessageId} content is identical to new chunk {i}, skipping edit.");
                            nextTgMessagesState.Add(existingMsg);
                        } else {
                            try {
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
                            } catch (ApiRequestException apiEx) when (apiEx.ErrorCode == 400 && apiEx.Message.Contains("message is not modified")) {
                                this.logger.LogInformation($"SynchronizeMessages: Message {existingMsg.MessageId} was not modified (API confirmation). Content identical.");
                                nextTgMessagesState.Add(existingMsg);
                                nextHtmlMap[existingMsg.MessageId] = htmlChunk;
                            } catch (Exception ex) {
                                this.logger.LogError(ex, $"SynchronizeMessages: Error editing TG message {existingMsg.MessageId} for chunk {i}. Adding existing to list.");
                                nextTgMessagesState.Add(existingMsg);
                            }
                        }
                    } else {
                        try {
                            await this.botClient.DeleteMessage(chatId, existingMsg.MessageId, cancellationToken: cancellationToken);
                            this.logger.LogInformation($"SynchronizeMessages: Deleted message {existingMsg.MessageId} for empty chunk {i}.");
                            nextHtmlMap.Remove(existingMsg.MessageId);
                        } catch (Exception ex) { this.logger.LogError(ex, $"SynchronizeMessages: Error deleting TG message {existingMsg.MessageId} for empty chunk {i}."); }
                    }
                } else {
                    if (!string.IsNullOrWhiteSpace(htmlChunk)) {
                        try {
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
                        } catch (Exception ex) { this.logger.LogError(ex, $"SynchronizeMessages: Error sending new TG message for chunk {i}."); }
                    }
                }
            }

            for (int i = newMarkdownChunks.Count; i < currentTgMessages.Count; i++) {
                if (currentTgMessages[i] == null || currentTgMessages[i].MessageId == 0) continue;
                try {
                    await this.botClient.DeleteMessage(chatId, currentTgMessages[i].MessageId, cancellationToken: cancellationToken);
                    this.logger.LogInformation($"SynchronizeMessages: Deleted superfluous message {currentTgMessages[i].MessageId}.");
                    nextHtmlMap.Remove(currentTgMessages[i].MessageId);
                } catch (Exception ex) { this.logger.LogError(ex, $"SynchronizeMessages: Error deleting superfluous TG message {currentTgMessages[i].MessageId}."); }
            }
            return (nextTgMessagesState, nextHtmlMap);
        }

        /// <summary>
        /// 构建数据库存储结果
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="originalReplyTo">原始回复消息ID</param>
        /// <param name="finalSentTgMessages">最终发送的Telegram消息</param>
        /// <param name="finalMarkdownChunks">最终的Markdown消息块</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>格式化后的消息列表</returns>
        private async Task<List<Model.Data.Message>> BuildResultForDbAsync(
            long chatId,
            int originalReplyTo,
            List<Message> finalSentTgMessages,
            List<string> finalMarkdownChunks,
            CancellationToken cancellationToken) {
            var resultMessagesForDb = new List<Model.Data.Message>();
            User botUser = null;

            for (int i = 0; i < finalSentTgMessages.Count; i++) {
                var tgMsg = finalSentTgMessages[i];
                if (tgMsg == null || tgMsg.MessageId == 0) continue;

                if (botUser == null) {
                    try {
                        botUser = await this.botClient.GetMe(cancellationToken: cancellationToken);
                    } catch (OperationCanceledException) { this.logger.LogInformation("BuildResultForDbAsync: GetMe cancelled."); break; } catch (Exception ex) { this.logger.LogError(ex, "BuildResultForDbAsync: Failed to get bot user info."); }
                }

                int msgReplyToId;
                if (i == 0) msgReplyToId = originalReplyTo;
                else if (i > 0 && finalSentTgMessages[i - 1] != null && finalSentTgMessages[i - 1].MessageId != 0)
                    msgReplyToId = finalSentTgMessages[i - 1].MessageId;
                else msgReplyToId = originalReplyTo;

                resultMessagesForDb.Add(new Model.Data.Message() {
                    GroupId = chatId,
                    MessageId = tgMsg.MessageId,
                    DateTime = tgMsg.Date.ToUniversalTime(),
                    Content = ( i < finalMarkdownChunks.Count ) ? finalMarkdownChunks[i] : "",
                    FromUserId = botUser?.Id ?? 0,
                    ReplyToMessageId = msgReplyToId,
                });
            }
            return resultMessagesForDb;
        }
        #endregion

        #region Simplified Streaming with Auto-HTML
        /// <summary>
        /// 简化版流式消息发送(自动HTML构建和分块)
        /// </summary>
        /// <param name="messages">异步消息流</param>
        /// <param name="chatId">目标聊天ID</param>
        /// <param name="replyTo">回复的消息ID</param>
        /// <param name="initialContent">初始占位内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送成功的消息列表</returns>
        public async Task<List<Model.Data.Message>> SendStreamingMessage(
            IAsyncEnumerable<string> messages,
            long chatId,
            int replyTo,
            string initialContent = "⏳",
            CancellationToken cancellationToken = default) {
            var sentMessages = new List<Model.Data.Message>();
            var builder = new StringBuilder();
            var currentMessage = await botClient.SendMessage(
                chatId: chatId,
                text: initialContent,
                replyParameters: new ReplyParameters { MessageId = replyTo },
                cancellationToken: cancellationToken);

            var lastUpdateTime = DateTime.MinValue;
            var updateInterval = TimeSpan.FromSeconds(1);
            var botUser = await botClient.GetMe(cancellationToken);
            var lastMessageId = currentMessage.MessageId;
            var dict = new Dictionary<int, (int PastMessageId, int CurrentMessageId, int CurrentLength)>();
            try {
                await foreach (var chunk in messages.WithCancellation(cancellationToken)) {
                    if (cancellationToken.IsCancellationRequested) break;

                    builder.Append(chunk);
                    var markdown = builder.ToString();
                    var html = MessageFormatHelper.ConvertMarkdownToTelegramHtml(markdown);

                    // 节流控制
                    if (DateTime.UtcNow - lastUpdateTime < updateInterval) {
                        continue;
                    }

                    // 自动分块处理
                    var chunks = MessageFormatHelper.SplitMarkdownIntoChunks(markdown, 1900);
                    if (chunks.Count == 0) continue;

                    try {
                        for (var i = 0; i < chunks.Count; i++) {
                            if (dict.TryGetValue(i, out var dictData)) {
                                if (chunks[i].Length == dictData.CurrentLength) {
                                    continue;
                                } else {
                                    await botClient.EditMessageText(
                                        chatId: chatId,
                                        messageId: dictData.CurrentMessageId,
                                        text: MessageFormatHelper.ConvertMarkdownToTelegramHtml(chunks[i]),
                                        parseMode: ParseMode.Html,
                                        cancellationToken: cancellationToken);
                                    lastUpdateTime = DateTime.UtcNow;
                                }

                            } else {
                                var newMessage = await botClient.SendMessage(
                                    chatId: chatId,
                                    text: MessageFormatHelper.ConvertMarkdownToTelegramHtml(chunks[i]),
                                    replyParameters: new ReplyParameters { MessageId = lastMessageId },
                                    parseMode: ParseMode.Html,
                                    cancellationToken: cancellationToken);
                                dict.Add(i, (lastMessageId, newMessage.MessageId, chunks[i].Length));

                                lastMessageId = newMessage.MessageId;
                                lastUpdateTime = DateTime.UtcNow;
                            }
                        }
                    } catch (Exception ex) {
                        logger.LogError(ex, "Error updating streaming message");
                    }
                }

                // 最终消息处理
                var finalMarkdown = builder.ToString();
                var finalChunks = MessageFormatHelper.SplitMarkdownIntoChunks(finalMarkdown, 4095);

                if (finalChunks.Count > 0) {
                    // 更新第一条消息
                    await botClient.EditMessageText(
                        chatId: chatId,
                        messageId: currentMessage.MessageId,
                        text: MessageFormatHelper.ConvertMarkdownToTelegramHtml(finalChunks[0]),
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken);

                    // 处理剩余分块
                    for (int i = 1; i < finalChunks.Count; i++) {
                        var newMessage = await botClient.SendMessage(
                            chatId: chatId,
                            text: MessageFormatHelper.ConvertMarkdownToTelegramHtml(finalChunks[i]),
                            replyParameters: new ReplyParameters { MessageId = lastMessageId },
                            parseMode: ParseMode.Html,
                            cancellationToken: cancellationToken);

                        lastMessageId = newMessage.MessageId;
                        sentMessages.Add(new Model.Data.Message {
                            GroupId = chatId,
                            MessageId = newMessage.MessageId,
                            DateTime = newMessage.Date,
                            Content = finalChunks[i],
                            FromUserId = botUser.Id,
                            ReplyToMessageId = lastMessageId
                        });
                    }
                }

                // 添加第一条消息到结果
                sentMessages.Add(new Model.Data.Message {
                    GroupId = chatId,
                    MessageId = currentMessage.MessageId,
                    DateTime = currentMessage.Date,
                    Content = finalChunks.Count > 0 ? finalChunks[0] : string.Empty,
                    FromUserId = botUser.Id,
                    ReplyToMessageId = replyTo
                });
            } catch (Exception ex) {
                logger.LogError(ex, "Error in streaming message handling");

                // 错误处理：发送原始内容作为纯文本
                if (builder.Length > 0) {
                    try {
                        var plainText = MessageFormatHelper.ConvertToPlainText(builder.ToString());
                        await botClient.SendMessage(
                            chatId: chatId,
                            text: $"Error occurred. Original content:\n{plainText}",
                            replyParameters: new ReplyParameters { MessageId = replyTo },
                            cancellationToken: cancellationToken);
                    } catch (Exception fallbackEx) {
                        logger.LogError(fallbackEx, "Failed to send fallback message");
                    }
                }
            }

            return sentMessages;
        }
        #endregion
    }
}

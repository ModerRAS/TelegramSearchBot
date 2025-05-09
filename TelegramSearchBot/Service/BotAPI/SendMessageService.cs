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
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using Markdig;
using Telegram.Bot.Exceptions;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
// System.Linq is already included via using System.Collections.Generic;
using System.Web;
using System.Threading; // For CancellationToken

namespace TelegramSearchBot.Service.BotAPI
{
    public class SendMessageService : IService
    {
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
        public async IAsyncEnumerable<Model.Data.Message> SendMessage(IAsyncEnumerable<string> messages, long ChatId, int replyTo, string InitialContent = "Initializing...", ParseMode parseMode = ParseMode.Html)
        {
            // 初始化一条消息，准备编辑
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
                    if (builder.Length > 1900) // Telegram message length limit is 4096, but we leave some buffer
                    {
                        tmpMessageId = sentMessage.MessageId;
                        var currentContent = builder.ToString();
                        messagesToYield.Add(new Model.Data.Message()
                        {
                            GroupId = ChatId,
                            MessageId = sentMessage.MessageId,
                            DateTime = sentMessage.Date,
                            ReplyToUserId = (await botClient.GetMe()).Id,
                            ReplyToMessageId = tmpMessageId,
                            FromUserId = (await botClient.GetMe()).Id,
                            Content = currentContent, // Store original Markdown/text
                        });
                        // Send current buffer and start a new message
                        await TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, currentContent, parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
                        sentMessage = await botClient.SendMessage(
                            chatId: ChatId,
                            text: InitialContent, // Placeholder for the new message
                            replyParameters: new ReplyParameters() { MessageId = tmpMessageId }
                        );
                        builder.Clear();
                    }
                    builder.Append(PerMessage);
                    if (DateTime.UtcNow - datetime > TimeSpan.FromSeconds(5)) // Edit message every 5 seconds
                    {
                        datetime = DateTime.UtcNow;
                        await TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, builder.ToString(), parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
                    }
                }
                // Send the final part of the message
                var finalContent = builder.ToString();
                await TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, finalContent, parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error sending streaming message to {ChatId}");
                // Fallback: send the remaining content as a new plain text message if something went wrong during streaming
                if (builder.Length > 0)
                {
                    try
                    {
                        // Attempt to send as plain text
                        var plainTextContent = ConvertToPlainText(builder.ToString());
                        await botClient.SendMessage(
                            chatId: ChatId,
                            text: "Message content could not be fully displayed due to an error. Partial content:\n" + plainTextContent,
                            replyParameters: new ReplyParameters() { MessageId = replyTo }
                        );
                    }
                    catch (Exception fallbackEx)
                    {
                        logger.LogError(fallbackEx, $"Error sending fallback plain text message to {ChatId}");
                    }
                }
            }

            messagesToYield.Add(new Model.Data.Message()
            {
                GroupId = ChatId,
                MessageId = sentMessage.MessageId,
                DateTime = sentMessage.Date,
                ReplyToUserId = (await botClient.GetMe()).Id,
                FromUserId = (await botClient.GetMe()).Id,
                ReplyToMessageId = tmpMessageId,
                Content = builder.ToString(),
            });

            foreach (var msg in messagesToYield)
            {
                yield return msg;
            }
        }

        private async Task TrySendMessageWithFallback(long chatId, int messageId, string originalMarkdownText, ParseMode preferredParseMode, bool isGroup, int replyToMessageId, string initialContentForNewMessage, bool isEdit)
        {
            string textToSend = originalMarkdownText;
            ParseMode currentParseMode = preferredParseMode;

            if (preferredParseMode == ParseMode.Html)
            {
                textToSend = ConvertMarkdownToTelegramHtml(originalMarkdownText);
            }
            // If preferredParseMode is MarkdownV2 or Markdown, we assume originalMarkdownText is already formatted.
            // Telegram.Bot library handles escaping for MarkdownV2. For legacy Markdown, it's as-is.

            try
            {
                if (isEdit)
                {
                    Message editedMessage = null;
                    await Send.AddTask(async () =>
                    {
                        editedMessage = await botClient.EditMessageText(
                            chatId: chatId,
                            messageId: messageId,
                            parseMode: currentParseMode,
                            text: textToSend
                        );
                    }, isGroup);

                    if (editedMessage != null && editedMessage.MessageId > 0)
                    {
                        logger.LogInformation($"Edited message {editedMessage.MessageId} successfully with {currentParseMode}.");
                    }
                    else // Silent fail for edit
                    {
                        logger.LogWarning($"Editing message {messageId} with {currentParseMode} failed silently or returned invalid object. Original Markdown: {originalMarkdownText}. Edit will be skipped to prevent overwriting with plaintext.");
                        // DO NOT CALL AttemptFallbackSend here for edits.
                    }
                }
                else // New message
                {
                    Message sentMsg = null;
                    await Send.AddTask(async () =>
                    {
                        sentMsg = await botClient.SendMessage(
                            chatId: chatId,
                            text: textToSend,
                            parseMode: currentParseMode,
                            replyParameters: new ReplyParameters() { MessageId = replyToMessageId }
                        );
                    }, isGroup);

                    if (sentMsg != null && sentMsg.MessageId > 0)
                    {
                        logger.LogInformation($"Sent new message {sentMsg.MessageId} successfully with {currentParseMode}.");
                    }
                    else // Silent fail for new message
                    {
                        logger.LogWarning($"Sending new message to {chatId} with {currentParseMode} failed silently or returned invalid object. Attempting fallback. Original Markdown: {originalMarkdownText}");
                        await AttemptFallbackSend(chatId, messageId, originalMarkdownText, isGroup, replyToMessageId, false, $"{currentParseMode} send failed silently"); // isEdit is false for new message
                    }
                }
            }
            catch (ApiRequestException apiEx) when (apiEx.Message.Contains("can't parse entities") || apiEx.Message.Contains("unclosed tag") || apiEx.ErrorCode == 400)
            {
                if (isEdit)
                {
                    logger.LogWarning(apiEx, $"Failed to edit message {messageId} with {currentParseMode} due to API error: {apiEx.Message}. Original Markdown: {originalMarkdownText}. Edit will be skipped.");
                    // DO NOT CALL AttemptFallbackSend here for edits.
                }
                else // New message failed
                {
                    logger.LogWarning(apiEx, $"Failed to send new message to {chatId} with {currentParseMode} due to API error: {apiEx.Message}. Attempting fallback. Original Markdown: {originalMarkdownText}");
                    await AttemptFallbackSend(chatId, messageId, originalMarkdownText, isGroup, replyToMessageId, false, apiEx.Message);
                }
            }
            catch (Exception ex)
            {
                if (isEdit)
                {
                    logger.LogError(ex, $"An unexpected error occurred while editing message {messageId} with {currentParseMode}. Original Markdown: {originalMarkdownText}. Edit will be skipped.");
                    // DO NOT CALL AttemptFallbackSend here for edits.
                }
                else // New message failed
                {
                    logger.LogError(ex, $"An unexpected error occurred while sending new message to {chatId} with {currentParseMode}. Original Markdown: {originalMarkdownText}. Attempting fallback.");
                    await AttemptFallbackSend(chatId, messageId, originalMarkdownText, isGroup, replyToMessageId, false, $"Unexpected error: {ex.Message}");
                }
            }
        }

        // AttemptFallbackSend is now only called for new messages if the initial HTML send fails.
        private async Task AttemptFallbackSend(long chatId, int messageId, string originalMarkdownText, bool isGroup, int replyToMessageId, bool wasEditAttempt, string initialFailureReason)
        {
            // Fallback 1: Try sending as plain text
            var plainText = ConvertToPlainText(originalMarkdownText);
            try
            {
                if (wasEditAttempt)
                {
                    await Send.AddTask(async () =>
                    {
                        var fallbackEditedMessage = await botClient.EditMessageText(
                            chatId: chatId,
                            messageId: messageId,
                            text: plainText // Send as plain text
                        );
                        logger.LogInformation($"Successfully resent message {fallbackEditedMessage.MessageId} as plain text after {initialFailureReason}.");
                    }, isGroup);
                }
                else // Was a new message attempt
                {
                    await Send.AddTask(async () =>
                    {
                        var fallbackSentMsg = await botClient.SendMessage(
                            chatId: chatId,
                            text: plainText, // Send as plain text
                            replyParameters: new ReplyParameters() { MessageId = replyToMessageId }
                        );
                        logger.LogInformation($"Successfully sent new message {fallbackSentMsg.MessageId} as plain text after {initialFailureReason}.");
                    }, isGroup);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to send/edit message to {chatId} (original messageId for edit: {messageId}) even as plain text. Initial failure: {initialFailureReason}. Original Markdown: {originalMarkdownText}");
                if (!wasEditAttempt) { // For new messages, send a generic error if plain text also fails
                    await Send.AddTask(async () =>
                    {
                        await botClient.SendMessage(
                            chatId: chatId,
                            text: "An error occurred while formatting the message. The content could not be displayed.",
                            replyParameters: new ReplyParameters() { MessageId = replyToMessageId }
                        );
                    }, isGroup);
                }
            }
        }

        private string ConvertMarkdownToTelegramHtml(string markdownText)
        {
            if (string.IsNullOrEmpty(markdownText)) return string.Empty;

            // Configure Markdig pipeline for HTML conversion if needed, or use default.
            // Default Markdig.Markdown.ToHtml(markdownText) should be a good starting point.
            // Telegram ignores unsupported HTML tags.
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var html = Markdig.Markdown.ToHtml(markdownText, pipeline);
            
            // Telegram specific HTML sanitization/simplification could be added here if necessary.
            // For example, ensuring only <b>, <i>, <u>, <s>, <tg-spoiler>, <a>, <code>, <pre> are used.
            // Markdig's default output is generally quite clean.
            // Let's add a simple regex to ensure newlines are <br> for better compatibility in some cases,
            // though Telegram usually handles newlines in <pre> and outside tags correctly.
            // html = html.Replace("\n", "<br />\n"); // This might be too aggressive. Let Telegram handle newlines.
            // --- NEW LOGIC BELOW ---

            // var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UsePipeTables().Build(); // Duplicate removed
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
                        case "b":
                        case "strong":
                            builder.Append("<b>");
                            ProcessChildren(node, builder);
                            builder.Append("</b>");
                            break;
                        case "i":
                        case "em":
                            builder.Append("<i>");
                            ProcessChildren(node, builder);
                            builder.Append("</i>");
                            break;
                        case "u":
                            builder.Append("<u>");
                            ProcessChildren(node, builder);
                            builder.Append("</u>");
                            break;
                        case "s":
                        case "strike":
                        case "del":
                            builder.Append("<s>");
                            ProcessChildren(node, builder);
                            builder.Append("</s>");
                            break;
                        case "tg-spoiler":
                            builder.Append("<tg-spoiler>");
                            ProcessChildren(node, builder);
                            builder.Append("</tg-spoiler>");
                            break;
                        case "a":
                            string href = node.GetAttributeValue("href", null);
                            if (!string.IsNullOrEmpty(href))
                            {
                                builder.Append($"<a href=\"{HttpUtility.HtmlEncode(href)}\">");
                                ProcessChildren(node, builder);
                                builder.Append("</a>");
                            }
                            else
                            {
                                ProcessChildren(node, builder); // Treat as plain text if no href
                            }
                            break;
                        case "code":
                            // If parent is <pre>, Telegram expects <pre><code class="language-xyz">
                            if (node.ParentNode != null && node.ParentNode.Name.ToLowerInvariant() == "pre")
                            {
                                string langClass = node.GetAttributeValue("class", "");
                                if (!string.IsNullOrEmpty(langClass) && langClass.StartsWith("language-"))
                                {
                                    builder.Append($"<code class=\"{HttpUtility.HtmlEncode(langClass)}\">");
                                }
                                else
                                {
                                    builder.Append("<code>");
                                }
                                builder.Append(HttpUtility.HtmlEncode(node.InnerText));
                                builder.Append("</code>");
                            }
                            else // Inline code
                            {
                                builder.Append("<code>");
                                builder.Append(HttpUtility.HtmlEncode(node.InnerText));
                                builder.Append("</code>");
                            }
                            break;
                        case "pre":
                            builder.Append("<pre>");
                            // Check if child is <code>, if so, it's handled by the 'code' case
                            // Otherwise, treat content of <pre> directly
                            if (node.ChildNodes.Count == 1 && node.FirstChild.Name.ToLowerInvariant() == "code")
                            {
                                ProcessHtmlNode(node.FirstChild, builder); // Will handle <pre><code>
                            }
                            else
                            {
                                builder.Append(HttpUtility.HtmlEncode(node.InnerText));
                            }
                            builder.Append("</pre>");
                            break;
                        case "table":
                            builder.Append(FormatHtmlTableAsPreformattedText(node));
                            break;
                        case "p":
                            ProcessChildren(node, builder);
                            builder.Append("\n"); // Add a newline after a paragraph
                            break;
                        case "br":
                            builder.Append("\n");
                            break;
                        case "hr":
                            builder.Append("\n――――――――――――\n"); // Represent hr as a line
                            break;
                        case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                            builder.Append("<b>"); // Treat headers as bold
                            ProcessChildren(node, builder);
                            builder.Append("</b>\n");
                            break;
                        case "ul":
                        case "ol":
                            ProcessList(node, builder, tagName == "ol" ? 1 : 0);
                            builder.Append("\n");
                            break;
                        case "blockquote":
                            // Add "> " to each line of the blockquote content
                            var blockquoteContent = new StringBuilder();
                            ProcessChildren(node, blockquoteContent);
                            var lines = blockquoteContent.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                builder.Append($"> {line}\n");
                            }
                            break;
                        // Tags to strip but process children (effectively treating content as plain text)
                        case "span":
                        case "div":
                        case "font":
                        case "img": // For img, just process alt text if available, or src as text
                            if (tagName == "img")
                            {
                                string alt = node.GetAttributeValue("alt", null);
                                string src = node.GetAttributeValue("src", null);
                                if (!string.IsNullOrEmpty(alt)) builder.Append($"[Image: {HttpUtility.HtmlEncode(alt)}] ");
                                else if (!string.IsNullOrEmpty(src)) builder.Append($"[Image: {HttpUtility.HtmlEncode(src)}] ");
                            }
                            ProcessChildren(node, builder);
                            break;
                        default: // Unsupported tags: strip tag, process children
                            ProcessChildren(node, builder);
                            break;
                    }
                    break;
                case HtmlNodeType.Text:
                    // Decode HTML entities and append
                    builder.Append(HttpUtility.HtmlEncode(HttpUtility.HtmlDecode(node.InnerText)));
                    break;
                case HtmlNodeType.Document:
                    ProcessChildren(node, builder);
                    break;
            }
        }

        private void ProcessChildren(HtmlNode parentNode, StringBuilder builder)
        {
            foreach (var childNode in parentNode.ChildNodes)
            {
                ProcessHtmlNode(childNode, builder);
            }
        }
        
        private void ProcessList(HtmlNode listNode, StringBuilder builder, int startNumber)
        {
            var items = listNode.SelectNodes("./li");
            if (items == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                if (startNumber > 0) // Ordered list
                {
                    builder.Append($"{startNumber + i}. ");
                }
                else // Unordered list
                {
                    builder.Append("• ");
                }
                // Process children of <li>, which might contain nested lists or other formatting
                var liContent = new StringBuilder();
                ProcessChildren(items[i], liContent);
                // Trim to avoid excessive newlines from <p> inside <li>
                builder.Append(liContent.ToString().Trim()); 
                builder.Append("\n");
            }
        }


        private string FormatHtmlTableAsPreformattedText(HtmlNode tableNode)
        {
            var rows = new List<List<string>>();
            var columnWidths = new List<int>();

            // Extract data and calculate column widths
            foreach (var rowNode in tableNode.SelectNodes(".//tr"))
            {
                var cells = new List<string>();
                int currentCellIndex = 0;
                foreach (var cellNode in rowNode.SelectNodes(".//th|.//td"))
                {
                    string cellText = HttpUtility.HtmlDecode(cellNode.InnerText.Trim());
                    cells.Add(cellText);
                    if (columnWidths.Count <= currentCellIndex)
                    {
                        columnWidths.Add(cellText.Length);
                    }
                    else
                    {
                        columnWidths[currentCellIndex] = Math.Max(columnWidths[currentCellIndex], cellText.Length);
                    }
                    currentCellIndex++;
                }
                rows.Add(cells);
            }

            if (!rows.Any()) return "";

            StringBuilder tableBuilder = new StringBuilder();
            tableBuilder.Append("<pre>");

            // Build header separator if there's a header (assuming first row is header if <th> exists)
            bool hasHeader = tableNode.SelectSingleNode(".//th") != null;
            
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                for (int j = 0; j < row.Count; j++)
                {
                    string cell = row[j];
                    // Pad cell content to match column width
                    tableBuilder.Append(cell.PadRight(columnWidths[j]));
                    if (j < row.Count - 1)
                    {
                        tableBuilder.Append(" | ");
                    }
                }
                tableBuilder.Append("\n");

                // Add a separator line after the header
                if (hasHeader && i == 0 && rows.Count > 1)
                {
                    for (int j = 0; j < columnWidths.Count; j++)
                    {
                        tableBuilder.Append(new string('-', columnWidths[j]));
                        if (j < columnWidths.Count - 1)
                        {
                            tableBuilder.Append("-+-");
                        }
                    }
                    tableBuilder.Append("\n");
                }
            }
            tableBuilder.Append("</pre>");
            return tableBuilder.ToString();
        }


        private string ConvertToPlainText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // First, convert Markdown to HTML (if it's Markdown)
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            string htmlText = Markdig.Markdown.ToHtml(text, pipeline);

            // Then, convert HTML to plain text using HtmlAgilityPack
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(htmlText);
            
            // Remove script and style tags first
            doc.DocumentNode.Descendants()
                .Where(n => n.Name == "script" || n.Name == "style")
                .ToList()
                .ForEach(n => n.Remove());

            // Get plain text
            StringBuilder plainTextBuilder = new StringBuilder();
            foreach (HtmlNode node in doc.DocumentNode.DescendantsAndSelf())
            {
                if (node.NodeType == HtmlNodeType.Text)
                {
                    if (node.ParentNode.Name != "script" && node.ParentNode.Name != "style")
                    {
                        plainTextBuilder.Append(HttpUtility.HtmlDecode(node.InnerText));
                    }
                }
                else if (node.Name == "br" || node.Name == "p" || node.Name == "div") // Add newlines for block elements
                {
                    plainTextBuilder.Append(" "); // Add a space before potential newline to separate words
                }
            }
            // Normalize whitespace and newlines
            string result = Regex.Replace(plainTextBuilder.ToString(), @"\s+", " ").Trim();
            result = Regex.Replace(result, @" ([\r\n])", "$1"); // Remove space before newline
            result = Regex.Replace(result, @"([\r\n]) ", "$1"); // Remove space after newline
            result = Regex.Replace(result, @"([\r\n]){2,}", "\n\n"); // Max 2 consecutive newlines
            return result.Trim();
        }

        // New method requested by user
        public async Task<List<Model.Data.Message>> SendFullMessageStream( // Changed return type
            IAsyncEnumerable<string> fullMessagesStream,
            long chatId,
            int replyTo,
            string initialPlaceholderContent = "⏳", // Brief placeholder
            CancellationToken cancellationToken = default) // Removed EnumeratorCancellation attribute as it's not IAsyncEnumerable anymore
        {
            List<Message> sentTelegramMessages = new List<Message>();
            DateTime lastApiCallTime = DateTime.MinValue;
            TimeSpan apiCallInterval = TimeSpan.FromSeconds(1.5); // Throttle API calls

            string currentFullMarkdown = string.Empty;
            List<string> finalMarkdownChunks = new List<string>(); // To store the chunks of the last processed full markdown
            bool isFirstMessageInStream = true;
            
            // No GetCurrentReplyToId helper needed here as reply logic is per-iteration based on nextIterationSentMessages

            await foreach (var markdownContentFromStream in fullMessagesStream.WithCancellation(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                currentFullMarkdown = markdownContentFromStream;

                // Throttle Telegram API calls
                var timeSinceLastCall = DateTime.UtcNow - lastApiCallTime;
                if (timeSinceLastCall < apiCallInterval && !isFirstMessageInStream)
                {
                    var delay = apiCallInterval - timeSinceLastCall;
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
                }
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                
                lastApiCallTime = DateTime.UtcNow;
                isFirstMessageInStream = false;

                List<string> markdownChunks = SplitMarkdownIntoChunks(currentFullMarkdown, 1900); // Adjusted maxLength to 1900
                if (!markdownChunks.Any() && !string.IsNullOrEmpty(currentFullMarkdown))
                {
                    markdownChunks.Add(string.Empty); // Represents clearing content
                }
                if (!markdownChunks.Any() && sentTelegramMessages.Count == 0) // Nothing to send, nothing was sent
                {
                    continue;
                }


                List<Message> nextIterationSentMessages = new List<Message>();
                int effectiveReplyTo = replyTo; // This will be updated to the ID of the previously sent segment

                for (int i = 0; i < markdownChunks.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                    string mdChunk = markdownChunks[i];
                    string htmlChunk = ConvertMarkdownToTelegramHtml(mdChunk);

                    // Determine the reply_to_message_id for the current segment
                    if (i == 0) effectiveReplyTo = replyTo; // First chunk replies to the original message
                    else if (nextIterationSentMessages.Any()) // Subsequent chunks reply to the previous chunk of this stream
                    {
                         var lastSentValidMsg = nextIterationSentMessages.LastOrDefault(m => m != null && m.MessageId != 0);
                         if(lastSentValidMsg != null) effectiveReplyTo = lastSentValidMsg.MessageId;
                         // else it remains what it was (e.g. original replyTo if all previous failed)
                    }


                    if (i < sentTelegramMessages.Count) // Existing Telegram message for this chunk index
                    {
                        Message existingTgMessage = sentTelegramMessages[i];
                        if (existingTgMessage == null || existingTgMessage.MessageId == 0) continue; // Should not happen if list is managed well

                        if (!string.IsNullOrWhiteSpace(htmlChunk))
                        {
                            Message editedMsg = null;
                            try
                            {
                                await Send.AddTask(async () => {
                                    editedMsg = await botClient.EditMessageText(
                                        chatId: chatId,
                                        messageId: existingTgMessage.MessageId,
                                        text: htmlChunk,
                                        parseMode: ParseMode.Html,
                                        cancellationToken: cancellationToken);
                                }, chatId < 0);
                                
                                if (editedMsg != null && editedMsg.MessageId != 0) {
                                    nextIterationSentMessages.Add(editedMsg);
                                } else {
                                    logger?.LogWarning($"Editing TG message {existingTgMessage.MessageId} for chunk {i} returned null or invalid message. Assuming it failed.");
                                     // Keep old message in list for next iteration's count if edit fails, or remove?
                                     // If we add existingTgMessage, it might be retried. For now, don't add if edit fails.
                                }
                            }
                            catch (Exception ex)
                            {
                                logger?.LogError(ex, $"Error editing TG message {existingTgMessage.MessageId} for chunk {i}. HTML: {htmlChunk.Substring(0, Math.Min(100, htmlChunk.Length))}");
                                // Don't add to nextIterationSentMessages if edit fails
                            }
                        }
                        else // htmlChunk is empty/whitespace, so delete the corresponding Telegram message
                        {
                            try
                            {
                                await Send.AddTask(async () => {
                                    await botClient.DeleteMessage(chatId, existingTgMessage.MessageId, cancellationToken: cancellationToken);
                                }, chatId < 0);
                            }
                            catch (Exception ex) { logger?.LogError(ex, $"Error deleting TG message {existingTgMessage.MessageId} for empty chunk {i}."); }
                        }
                    }
                    else // New chunk, need to send a new Telegram message
                    {
                        if (!string.IsNullOrWhiteSpace(htmlChunk))
                        {
                            Message newTgMsg = null;
                            try
                            {
                                await Send.AddTask(async () => {
                                    newTgMsg = await botClient.SendMessage(
                                        chatId: chatId,
                                        text: htmlChunk,
                                        parseMode: ParseMode.Html,
                                        replyParameters: new ReplyParameters { MessageId = effectiveReplyTo },
                                        cancellationToken: cancellationToken);
                                }, chatId < 0);

                                if (newTgMsg != null && newTgMsg.MessageId != 0) {
                                   nextIterationSentMessages.Add(newTgMsg);
                                } else {
                                     logger?.LogWarning($"Sending new TG message for chunk {i} returned null or invalid message.");
                                }
                            }
                            catch (Exception ex) { logger?.LogError(ex, $"Error sending new TG message for chunk {i}. HTML: {htmlChunk.Substring(0, Math.Min(100, htmlChunk.Length))}"); }
                        }
                    }
                }

                // Delete any remaining old Telegram messages if the new content has fewer chunks
                for (int i = markdownChunks.Count; i < sentTelegramMessages.Count; i++)
                {
                    if (sentTelegramMessages[i] == null || sentTelegramMessages[i].MessageId == 0) continue;
                    try
                    {
                         await Send.AddTask(async () => {
                            await botClient.DeleteMessage(chatId, sentTelegramMessages[i].MessageId, cancellationToken: cancellationToken);
                         }, chatId < 0);
                    }
                    catch (Exception ex) { logger?.LogError(ex, $"Error deleting superfluous TG message {sentTelegramMessages[i].MessageId}."); }
                }
                
                sentTelegramMessages = new List<Message>(nextIterationSentMessages); // Update the main list with successfully sent/edited messages

                // Store the chunks from the current (potentially last) fullMarkdownContent
                finalMarkdownChunks = markdownChunks; 
                 if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
            }

            // After the loop, construct the result list based on the final state
            var resultMessagesForDb = new List<Model.Data.Message>();
            User botUser = null; // Cache bot user info

            for(int i = 0; i < sentTelegramMessages.Count; i++)
            {
                var tgMsg = sentTelegramMessages[i];
                if (tgMsg == null || tgMsg.MessageId == 0) continue;

                if (botUser == null) // Get bot user info once
                {
                    try
                    {
                        await Send.AddTask(async () => {
                             botUser = await botClient.GetMe(cancellationToken: cancellationToken);
                        }, chatId < 0); // Assuming chatId < 0 for group context for Send.AddTask
                    }
                    catch(Exception ex)
                    {
                        logger.LogError(ex, "Failed to get bot user info for DB logging.");
                        // Potentially throw or handle, for now, FromUserId might be missing or default
                    }
                }

                int msgReplyToId;
                if (i == 0) msgReplyToId = replyTo;
                else if (i > 0 && sentTelegramMessages[i-1] != null && sentTelegramMessages[i-1].MessageId != 0) msgReplyToId = sentTelegramMessages[i-1].MessageId;
                else msgReplyToId = replyTo; 

                resultMessagesForDb.Add(new Model.Data.Message() {
                    GroupId = chatId,
                    MessageId = tgMsg.MessageId,
                    DateTime = tgMsg.Date.ToUniversalTime(),
                    Content = (i < finalMarkdownChunks.Count) ? finalMarkdownChunks[i] : "", // Use final chunks
                    FromUserId = botUser?.Id ?? 0, // Use cached botUser.Id
                    ReplyToMessageId = msgReplyToId,
                });
            }
            return resultMessagesForDb;
        }

        private List<string> SplitMarkdownIntoChunks(string markdown, int maxLength)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(markdown)) return chunks;
            if (maxLength <= 0) { // Safety check
                chunks.Add(markdown);
                return chunks;
            }

            int currentPosition = 0;
            while (currentPosition < markdown.Length)
            {
                int lengthToTake = Math.Min(maxLength, markdown.Length - currentPosition);
                
                string chunk;
                if (markdown.Length - currentPosition <= maxLength) // Last chunk or fits entirely
                {
                    chunk = markdown.Substring(currentPosition);
                    lengthToTake = chunk.Length; // Actual length of the last chunk
                }
                else
                {
                    // Try to find a good split point within lengthToTake
                    int splitPoint = -1;
                    // Prefer to split at double newlines (paragraph end) backwards from lengthToTake
                    for (int i = lengthToTake - 2; i > maxLength / 3; i--) // Search in the latter 2/3
                    {
                        if (markdown[currentPosition + i] == '\n' && markdown[currentPosition + i + 1] == '\n')
                        {
                            splitPoint = i + 2; // Split after the double newline
                            break;
                        }
                    }

                    if (splitPoint == -1) // If no double newline, try single newline
                    {
                        for (int i = lengthToTake - 1; i > maxLength / 3; i--)
                        {
                            if (markdown[currentPosition + i] == '\n')
                            {
                                splitPoint = i + 1; // Split after the newline
                                break;
                            }
                        }
                    }
                    
                    if (splitPoint != -1) lengthToTake = splitPoint;
                    // If no good split point found, it will be a hard split at maxLength
                    chunk = markdown.Substring(currentPosition, lengthToTake);
                }
                
                chunks.Add(chunk);
                currentPosition += lengthToTake;
            }
            return chunks;
        }
    }
}

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
using System.Linq;
using System.Web;

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
    }
}

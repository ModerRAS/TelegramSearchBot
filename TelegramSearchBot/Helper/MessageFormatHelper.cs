using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Markdig;

namespace TelegramSearchBot.Helper {
    public static class MessageFormatHelper {
        public static string ConvertMarkdownToTelegramHtml(string markdownText) {
            if (string.IsNullOrEmpty(markdownText)) return string.Empty;
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UsePipeTables().Build();
            string rawHtml = Markdig.Markdown.ToHtml(markdownText, pipeline);
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);
            StringBuilder telegramHtmlBuilder = new StringBuilder();
            ProcessHtmlNode(doc.DocumentNode, telegramHtmlBuilder);
            return telegramHtmlBuilder.ToString().Trim();
        }

        private static void ProcessHtmlNode(HtmlNode node, StringBuilder builder) {
            switch (node.NodeType) {
                case HtmlNodeType.Element:
                    string tagName = node.Name.ToLowerInvariant();
                    switch (tagName) {
                        case "b": case "strong": builder.Append("<b>"); ProcessChildren(node, builder); builder.Append("</b>"); break;
                        case "i": case "em": builder.Append("<i>"); ProcessChildren(node, builder); builder.Append("</i>"); break;
                        case "u": builder.Append("<u>"); ProcessChildren(node, builder); builder.Append("</u>"); break;
                        case "s": case "strike": case "del": builder.Append("<s>"); ProcessChildren(node, builder); builder.Append("</s>"); break;
                        case "tg-spoiler": builder.Append("<tg-spoiler>"); ProcessChildren(node, builder); builder.Append("</tg-spoiler>"); break;
                        case "a":
                            string href = node.GetAttributeValue("href", null);
                            if (!string.IsNullOrEmpty(href)) { builder.Append($"<a href=\"{HttpUtility.HtmlEncode(href)}\">"); ProcessChildren(node, builder); builder.Append("</a>"); } else { ProcessChildren(node, builder); }
                            break;
                        case "code":
                            if (node.ParentNode != null && node.ParentNode.Name.ToLowerInvariant() == "pre") {
                                string langClass = node.GetAttributeValue("class", "");
                                if (!string.IsNullOrEmpty(langClass) && langClass.StartsWith("language-")) builder.Append($"<code class=\"{HttpUtility.HtmlEncode(langClass)}\">");
                                else builder.Append("<code>");
                                builder.Append(HttpUtility.HtmlEncode(node.InnerText));
                                builder.Append("</code>");
                            } else { builder.Append("<code>"); builder.Append(HttpUtility.HtmlEncode(node.InnerText)); builder.Append("</code>"); }
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
                        case "span":
                        case "div":
                        case "font":
                        case "img":
                            if (tagName == "img") {
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

        private static void ProcessChildren(HtmlNode parentNode, StringBuilder builder) {
            foreach (var childNode in parentNode.ChildNodes) ProcessHtmlNode(childNode, builder);
        }

        private static void ProcessList(HtmlNode listNode, StringBuilder builder, int startNumber) {
            var items = listNode.SelectNodes("./li");
            if (items == null) return;
            for (int i = 0; i < items.Count; i++) {
                if (startNumber > 0) builder.Append($"{startNumber + i}. ");
                else builder.Append("• ");
                var liContent = new StringBuilder(); ProcessChildren(items[i], liContent);
                builder.Append(liContent.ToString().Trim());
                builder.Append("\n");
            }
        }

        private static string FormatHtmlTableAsPreformattedText(HtmlNode tableNode) {
            var rows = new List<List<string>>(); var columnWidths = new List<int>();
            foreach (var rowNode in tableNode.SelectNodes(".//tr")) {
                var cells = new List<string>(); int currentCellIndex = 0;
                foreach (var cellNode in rowNode.SelectNodes(".//th|.//td")) {
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
            for (int i = 0; i < rows.Count; i++) {
                var row = rows[i];
                for (int j = 0; j < row.Count; j++) {
                    tableBuilder.Append(row[j].PadRight(columnWidths[j]));
                    if (j < row.Count - 1) tableBuilder.Append(" | ");
                }
                tableBuilder.Append("\n");
                if (hasHeader && i == 0 && rows.Count > 1) {
                    for (int j = 0; j < columnWidths.Count; j++) {
                        tableBuilder.Append(new string('-', columnWidths[j]));
                        if (j < columnWidths.Count - 1) tableBuilder.Append("-+-");
                    }
                    tableBuilder.Append("\n");
                }
            }
            tableBuilder.Append("</pre>"); return tableBuilder.ToString();
        }

        public static string ConvertToPlainText(string text) {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            string htmlText = Markdig.Markdown.ToHtml(text, pipeline);
            HtmlDocument doc = new HtmlDocument(); doc.LoadHtml(htmlText);
            doc.DocumentNode.Descendants().Where(n => n.Name == "script" || n.Name == "style").ToList().ForEach(n => n.Remove());
            StringBuilder plainTextBuilder = new StringBuilder();
            foreach (HtmlNode node in doc.DocumentNode.DescendantsAndSelf()) {
                if (node.NodeType == HtmlNodeType.Text) { if (node.ParentNode.Name != "script" && node.ParentNode.Name != "style") plainTextBuilder.Append(HttpUtility.HtmlDecode(node.InnerText)); } else if (node.Name == "br" || node.Name == "p" || node.Name == "div") plainTextBuilder.Append(" ");
            }
            string result = Regex.Replace(plainTextBuilder.ToString(), @"\s+", " ").Trim();
            result = Regex.Replace(result, @" ([\r\n])", "$1");
            result = Regex.Replace(result, @"([\r\n]) ", "$1");
            result = Regex.Replace(result, @"([\r\n]){2,}", "\n\n");
            return result.Trim();
        }

        public static List<string> SplitMarkdownIntoChunks(string markdown, int maxLength) {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(markdown)) return chunks;
            if (maxLength <= 0) { chunks.Add(markdown); return chunks; }
            int currentPosition = 0;
            while (currentPosition < markdown.Length) {
                int lengthToTake = Math.Min(maxLength, markdown.Length - currentPosition);
                string chunk;
                if (markdown.Length - currentPosition <= maxLength) { chunk = markdown.Substring(currentPosition); lengthToTake = chunk.Length; } else {
                    int splitPoint = -1;
                    for (int i = lengthToTake - 2; i > maxLength / 3; i--) { if (markdown[currentPosition + i] == '\n' && markdown[currentPosition + i + 1] == '\n') { splitPoint = i + 2; break; } }
                    if (splitPoint == -1) { for (int i = lengthToTake - 1; i > maxLength / 3; i--) { if (markdown[currentPosition + i] == '\n') { splitPoint = i + 1; break; } } }
                    if (splitPoint != -1) lengthToTake = splitPoint;
                    chunk = markdown.Substring(currentPosition, lengthToTake);
                }
                chunks.Add(chunk); currentPosition += lengthToTake;
            }
            return chunks;
        }

        public static string EscapeMarkdownV2(string text) {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            char[] markdownV2EscapeChars = { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
            foreach (char c in markdownV2EscapeChars) {
                text = text.Replace(c.ToString(), "\\" + c);
            }
            return text;
        }
    }
}

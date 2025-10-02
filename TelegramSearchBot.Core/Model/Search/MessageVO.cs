using System;
using System.Linq;
using TelegramSearchBot.Core.Helper;
using TelegramSearchBot.Core.Model.Data;

namespace TelegramSearchBot.Core.Model.Search;

public class MessageVO {
    public long GroupId { get; set; }
    public long MessageId { get; set; }
    public string Content { get; set; }
    public MessageVO(Message message, string filter = null) {
        GroupId = message.GroupId;
        MessageId = message.MessageId;
        if (string.IsNullOrEmpty(message.Content)) {
            // 如果 message.Content 为空，那么从 message.MessageExtensions 中寻找 filter 的匹配词
            const int totalSnippet = 30;
            if (message.MessageExtensions != null && message.MessageExtensions.Any()) {
                // 优先在扩展的 Value 中查找包含 filter 的项
                if (!string.IsNullOrEmpty(filter)) {
                    var match = message.MessageExtensions.FirstOrDefault(e => !string.IsNullOrEmpty(e.Value) && e.Value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) {
                        Content = MessageFormatHelper.ExtractSnippetAroundFilter(match.Value, filter, totalSnippet);
                    } else {
                        // 未命中任何扩展，回退到把所有扩展拼接后的预览
                        var all = string.Join(" ", message.MessageExtensions.Select(e => ( string.IsNullOrEmpty(e.Name) ? "" : e.Name + ": " ) + ( e.Value ?? "" )).Where(s => !string.IsNullOrEmpty(s)));
                        Content = string.IsNullOrEmpty(all) ? string.Empty : MessageFormatHelper.ExtractSnippetAroundFilter(all, null, totalSnippet);
                    }
                } else {
                    // 没有 filter，使用第一个非空扩展的值或拼接后的预览
                    var first = message.MessageExtensions.FirstOrDefault(e => !string.IsNullOrEmpty(e.Value));
                    if (first != null) Content = MessageFormatHelper.ExtractSnippetAroundFilter(first.Value, null, totalSnippet);
                    else {
                        var all = string.Join(" ", message.MessageExtensions.Select(e => ( string.IsNullOrEmpty(e.Name) ? "" : e.Name + ": " ) + ( e.Value ?? "" )).Where(s => !string.IsNullOrEmpty(s)));
                        Content = string.IsNullOrEmpty(all) ? string.Empty : MessageFormatHelper.ExtractSnippetAroundFilter(all, null, totalSnippet);
                    }
                }
            } else {
                Content = string.Empty;
            }
        } else {
            // 如果有过滤词，找到裁剪过滤词前后共30个字符
            if (!string.IsNullOrEmpty(filter)) {
                Content = MessageFormatHelper.ExtractSnippetAroundFilter(message.Content, filter, 30);
            } else {
                Content = message.Content.Length <= 30 ? message.Content : message.Content.Substring(0, 30) + "...";
            }
        }
    }
}

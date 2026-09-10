using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Common;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// Projects neutral <see cref="AgentHistoryMessage"/> rows into normalized
    /// <see cref="LlmMessage"/> history. Replaces the four per-provider projection loops
    /// (which differed only cosmetically in reply/colon punctuation).
    /// Grouping rule shared by all legacy implementations: consecutive messages from the
    /// same side (bot vs user) accumulate into one message; leading bot messages are skipped.
    /// </summary>
    public static class LlmHistoryProjector {
        public static List<LlmMessage> Project(
            IReadOnlyList<AgentHistoryMessage> rows,
            bool supportsVision,
            ILogger? logger = null) {
            var result = new List<LlmMessage>();
            var str = new StringBuilder();
            var pendingImages = new List<byte[]>();
            AgentHistoryMessage? previous = null;

            void Flush() {
                if (previous == null || str.Length == 0) {
                    str.Clear();
                    pendingImages.Clear();
                    return;
                }
                byte[]? image = null;
                if (supportsVision && pendingImages.Count > 0) {
                    image = pendingImages[0];
                }
                result.Add(LlmMessage.User(str.ToString(), image, image != null ? "image/png" : null));
                str.Clear();
                pendingImages.Clear();
            }

            foreach (var row in rows) {
                if (previous == null && str.Length == 0 && row.FromUserId == Env.BotId) {
                    previous = row;
                    continue;
                }

                if (previous != null && ( previous.FromUserId == Env.BotId ) != ( row.FromUserId == Env.BotId )) {
                    Flush();
                }

                str.Append($"[{row.DateTime:yyyy-MM-dd HH:mm:ss zzz}]");
                str.Append(LlmHistoryFormat.FormatSender(row.User, row.FromUserId));
                if (row.ReplyToMessageId != 0) {
                    str.Append($"（Reply to msg {row.ReplyToMessageId}）");
                }
                str.Append('：').Append(row.Content ?? string.Empty).Append('\n');

                if (row.Extensions is { Count: > 0 }) {
                    str.Append("[扩展信息：");
                    foreach (var ext in row.Extensions) {
                        str.Append($"{ext.Name}={ext.Value}; ");
                    }
                    str.Append("]\n");
                }

                if (supportsVision && row.FromUserId != Env.BotId) {
                    var imageBytes = LlmPhotoLoader.TryLoadMessagePhoto(row.GroupId, row.MessageId, logger);
                    if (imageBytes != null) {
                        pendingImages.Add(imageBytes);
                    }
                }

                previous = row;
            }

            Flush();
            return result;
        }
    }
}

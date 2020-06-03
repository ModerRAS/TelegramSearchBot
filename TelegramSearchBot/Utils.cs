using System;
using System.Collections.Generic;
using System.Text;
using TelegramSearchBot.Model;

namespace TelegramSearchBot {
    class Utils {
        public static List<string> ConvertToList(IEnumerable<Message> messages, long ChatId) {
            var list = new List<string>();
            foreach (var kv in messages) {
                string text;
                if (kv.Content.Length > 15) {
                    text = kv.Content.Substring(0, 15);
                } else {
                    text = kv.Content;
                }
                list.Add($"[{text.Replace("\n", "").Replace("\r", "")}](https://t.me/c/{kv.GroupId.ToString().Substring(4)}/{kv.MessageId})");
            }
            return list;

        }
    }
}

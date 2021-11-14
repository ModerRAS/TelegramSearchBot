using System;
using System.Collections.Generic;
using System.IO;
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
        public static bool CreateDirectorys(string FolderPath) {
            try {
                var FolderPaths = FolderPath.Split('/');
                var tmp = new StringBuilder();
                foreach (var i in FolderPaths) {
                    tmp.Append(i);
                    tmp.Append("/");
                    if (!Directory.Exists(tmp.ToString())) {
                        Directory.CreateDirectory(tmp.ToString());
                    }
                }
                return true;
            } catch (Exception e) {
                Console.WriteLine(e);
                return false;
            }
        }
    }
}

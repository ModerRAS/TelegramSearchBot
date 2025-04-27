using Markdig;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Text;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot
{
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
        public static void CheckExistsAndCreateDirectorys(string FolderPath) {
            if (!Directory.Exists(FolderPath)) {
                CreateDirectorys(FolderPath);
            }
        }
        /// <summary>
        /// 检查 Markdown 是否符合标准（使用 Markdig 解析）。
        /// </summary>
        /// <param name="markdown">要验证的 Markdown 文本</param>
        /// <returns>如果 Markdown 合法，则返回 true；否则返回 false</returns>
        public static bool IsValidMarkdown(string markdown) {
            if (string.IsNullOrWhiteSpace(markdown))
                return false;

            try {
                var pipeline = new MarkdownPipelineBuilder().Build();
                string html = Markdown.ToHtml(markdown, pipeline);
                return !string.IsNullOrWhiteSpace(html); // 确保能成功转换为 HTML
            } catch {
                return false; // 解析失败，说明 Markdown 语法错误
            }
        }

        /// <summary>
        /// 转义 Markdown 以适配 Telegram 的 MarkdownV2 解析器。
        /// </summary>
        /// <param name="markdown">原始 Markdown 文本</param>
        /// <returns>适配 Telegram 的 MarkdownV2 格式文本</returns>
        public static string EscapeForTelegramMarkdownV2(string markdown) {
            if (string.IsNullOrWhiteSpace(markdown))
                return markdown;

            string specialChars = "_*[]()~`>#+-=|{}.!";

            foreach (char c in specialChars) {
                markdown = markdown.Replace(c.ToString(), "\\" + c);
            }

            return markdown;
        }

        public static int GetRandomAvailablePort() {
            // 尝试使用一个随机端口
            TcpListener listener = null;
            try {
                // 创建一个监听器，绑定到任意端口
                listener = new TcpListener(IPAddress.Loopback, 0); // 0表示随机端口
                listener.Start();

                // 获取分配的端口号
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                return port;
            } catch (Exception) {
                // 若端口绑定失败，则返回-1
                return -1;
            } finally {
                // 关闭监听器
                listener?.Stop();
            }
        }
    }
}

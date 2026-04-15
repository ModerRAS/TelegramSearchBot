using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TelegramSearchBot.Helper {
    /// <summary>
    /// 词云文本过滤器 - 过滤不适合词云展示的内容
    /// </summary>
    public static class WordCloudTextFilter {
        // URL正则：匹配 http/https/tg/magnet 等链接
        private static readonly Regex UrlRegex = new Regex(
            @"(https?://[^\s\u4e00-\u9fa5]+)" +  // HTTP/HTTPS链接
            @"|(tg://[^\s]+)" +                    // Telegram链接
            @"|(t\.me/[^\s]+)" +                  // t.me短链接
            @"|(magnet:[^\s]+)" +                 // 磁力链接
            @"|(www\.[^\s\u4e00-\u9fa5]+)",      // www开头的链接
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Base64检测正则：检测长串Base64字符（通常40+字符的字母数字组合）
        private static readonly Regex Base64Regex = new Regex(
            @"^[A-Za-z0-9+/]{40,}={0,2}$",
            RegexOptions.Compiled);

        // URL编码检测：检测是否包含百分号编码
        private static readonly Regex UrlEncodingRegex = new Regex(
            @"%[0-9A-Fa-f]{2}",
            RegexOptions.Compiled);

        // 纯数字/纯符号检测（排除太短的）
        private static readonly Regex PureDigitRegex = new Regex(
            @"^\d+$",
            RegexOptions.Compiled);

        // 需要排除的扩展名称（Alt/URL等非人类阅读内容）
        private static readonly HashSet<string> ExcludedExtensionNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase) {
            "alt",
            "alt_result",
            "qr_result",      // 二维码通常是链接
            "url",
            "photo_url",
            "file_url",
            "video_url",
            "audio_url",
            "thumb_url",
            "link",
            "source_url",
            "preview_url"
        };

        /// <summary>
        /// 过滤文本中的不相关内容，返回过滤后的文本
        /// </summary>
        /// <param name="text">原始文本</param>
        /// <returns>过滤后的文本，如果不适合词云使用则返回空字符串</returns>
        public static string FilterText(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return string.Empty;
            }

            // 1. 移除URL
            text = UrlRegex.Replace(text, " ");

            // 2. 检测是否包含URL编码（可能是参数或加密内容）
            if (ContainsExcessiveUrlEncoding(text)) {
                return string.Empty;
            }

            // 3. 检测是否为纯Base64（通常是加密数据或文件内容）
            if (IsBase64Content(text)) {
                return string.Empty;
            }

            // 4. 清理多余空白
            text = CleanWhitespace(text);

            // 5. 过滤纯数字（通常是ID、时间戳等）
            if (PureDigitRegex.IsMatch(text) || text.Length < 2) {
                return string.Empty;
            }

            // 6. 再次检查清理后是否为空
            if (string.IsNullOrWhiteSpace(text)) {
                return string.Empty;
            }

            return text;
        }

        /// <summary>
        /// 判断扩展名称是否应该被包含在词云中
        /// </summary>
        /// <param name="extensionName">扩展名称</param>
        /// <returns>如果应该包含返回true</returns>
        public static bool ShouldIncludeExtension(string extensionName) {
            if (string.IsNullOrWhiteSpace(extensionName)) {
                return false;
            }

            return !ExcludedExtensionNames.Contains(extensionName);
        }

        /// <summary>
        /// 批量过滤文本数组
        /// </summary>
        /// <param name="texts">原始文本数组</param>
        /// <returns>过滤后的文本数组</returns>
        public static string[] FilterTexts(IEnumerable<string> texts) {
            if (texts == null) {
                return Array.Empty<string>();
            }

            return texts
                .Select(FilterText)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToArray();
        }

        /// <summary>
        /// 检测文本是否包含过多URL编码（通常是API参数、加密数据等）
        /// </summary>
        private static bool ContainsExcessiveUrlEncoding(string text) {
            // 计算URL编码的出现次数
            int urlEncodingCount = UrlEncodingRegex.Matches(text).Count;
            
            // 如果文本中URL编码超过5个，或者超过文本长度的20%，认为是不可读内容
            if (urlEncodingCount > 5) {
                return true;
            }

            // 短文本中有URL编码可能是正常的（如%E5%93%88），长文本中过多则不正常
            if (text.Length > 50 && urlEncodingCount > text.Length / 20) {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 检测文本是否为Base64内容
        /// </summary>
        private static bool IsBase64Content(string text) {
            var trimmed = text.Trim();
            
            // 太短的不是Base64内容
            if (trimmed.Length < 40) {
                return false;
            }

            // 检查是否符合Base64模式（主要是字母数字+/=）
            if (Base64Regex.IsMatch(trimmed)) {
                return true;
            }

            // 检查是否有过多的Base64特征字符
            int base64Chars = trimmed.Count(c => c == '+' || c == '/' || c == '=');
            double ratio = (double)base64Chars / trimmed.Length;
            
            // Base64中+/=通常占比较固定
            if (ratio > 0.05 && ratio < 0.35 && trimmed.Length > 60) {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 清理多余空白字符
        /// </summary>
        private static string CleanWhitespace(string text) {
            // 将多个空白字符替换为单个空格
            var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            
            // 去除首尾空白
            return cleaned.Trim();
        }
    }
}

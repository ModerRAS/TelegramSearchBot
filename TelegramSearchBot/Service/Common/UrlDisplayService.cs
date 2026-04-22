using System;
using System.Linq;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Service.Common {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class UrlDisplayService : IService {
        private const int DefaultMaxDisplayLength = 60;

        public string ServiceName => nameof(UrlDisplayService);

        public bool IsUrlOnlyMessage(string text) {
            return TryGetStandaloneUrl(text, out _);
        }

        public bool TryFormatUrlOnlyMessage(string text, out string markdownText) {
            markdownText = string.Empty;
            if (!TryGetStandaloneUrl(text, out var url)) {
                return false;
            }

            var label = EscapeMarkdownLinkText(BuildDisplayLabel(url));
            markdownText = $"[打开链接：{label}](<{url}>)";
            return true;
        }

        public string BuildDisplayLabel(string url, int maxLength = DefaultMaxDisplayLength) {
            if (string.IsNullOrWhiteSpace(url)) {
                return string.Empty;
            }

            var trimmedUrl = url.Trim();
            if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)) {
                return Truncate(trimmedUrl, maxLength);
            }

            var builder = uri.Host;
            var lastSegment = uri.Segments
                .Select(static segment => segment.Trim('/'))
                .LastOrDefault(static segment => !string.IsNullOrWhiteSpace(segment));

            if (!string.IsNullOrWhiteSpace(lastSegment)) {
                builder += "/" + Uri.UnescapeDataString(lastSegment);
            } else if (!string.IsNullOrWhiteSpace(uri.AbsolutePath) && uri.AbsolutePath != "/") {
                builder += uri.AbsolutePath;
            }

            if (!string.IsNullOrWhiteSpace(uri.Query)) {
                builder += "?...";
            }

            if (!string.IsNullOrWhiteSpace(uri.Fragment)) {
                builder += "#...";
            }

            return Truncate(builder, maxLength);
        }

        private static bool TryGetStandaloneUrl(string text, out string url) {
            url = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }

            var trimmed = text.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out _)) {
                return false;
            }

            url = trimmed;
            return true;
        }

        private static string EscapeMarkdownLinkText(string text) {
            return text
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("[", "\\[", StringComparison.Ordinal)
                .Replace("]", "\\]", StringComparison.Ordinal);
        }

        private static string Truncate(string value, int maxLength) {
            if (value.Length <= maxLength) {
                return value;
            }

            if (maxLength <= 3) {
                return value.Substring(0, maxLength);
            }

            return value.Substring(0, maxLength - 3) + "...";
        }
    }
}

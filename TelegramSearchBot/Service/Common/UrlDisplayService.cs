using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.Service.Common {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class UrlDisplayService : IService {
        public string ServiceName => nameof(UrlDisplayService);

        private readonly ILogger<UrlDisplayService> _logger;
        private const int DefaultMaxLength = 80;
        private const int DomainMaxLength = 30;

        public UrlDisplayService(ILogger<UrlDisplayService> logger) {
            _logger = logger;
        }

        public string TruncateUrl(string url, int maxLength = DefaultMaxLength) {
            if (string.IsNullOrWhiteSpace(url)) {
                return url;
            }

            if (url.Length <= maxLength) {
                return url;
            }

            try {
                var uri = new Uri(url);
                var domain = uri.Host;
                
                if (domain.Length > DomainMaxLength) {
                    domain = domain.Substring(0, DomainMaxLength) + "...";
                }

                var path = uri.AbsolutePath;
                if (path.Length > 20) {
                    path = "..." + path.Substring(path.Length - 20);
                }

                return $"{uri.Scheme}://{domain}{path}";
            } catch (UriFormatException) {
                var truncated = url.Substring(0, maxLength - 3) + "...";
                return truncated;
            }
        }

        public string FormatUrlForDisplay(string text, bool disablePreview = false) {
            if (string.IsNullOrWhiteSpace(text)) {
                return text;
            }

            var urlPattern = @"(https?://[^\s]+)";
            var result = Regex.Replace(text, urlPattern, match => {
                var url = match.Value;
                if (url.Length > DefaultMaxLength) {
                    return TruncateUrl(url);
                }
                return url;
            });

            return result;
        }

        public bool IsUrlOnlyMessage(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }

            var trimmed = text.Trim();
            return Uri.TryCreate(trimmed, UriKind.Absolute, out _);
        }

        public string GetMessagePreview(string text, int maxLines = 3) {
            if (string.IsNullOrWhiteSpace(text)) {
                return text;
            }

            var lines = text.Split('\n');
            if (lines.Length <= maxLines) {
                return FormatUrlForDisplay(text);
            }

            var preview = string.Join("\n", lines, 0, maxLines);
            return FormatUrlForDisplay(preview) + "\n...";
        }
    }
}

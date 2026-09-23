using System;
using System.Text.RegularExpressions;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>Turns preset setup answers into the Gateway stored on LLMChannel. No new columns.</summary>
    public static class LlmPresetEndpoints {
        private static readonly Regex Token = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$", RegexOptions.Compiled);
        private static readonly Regex Region = new("^[a-z0-9-]{1,40}$", RegexOptions.Compiled);

        public static string Compose(LlmProviderPreset preset, string redisData) {
            var parts = (redisData ?? string.Empty).Split('|');
            return preset.Setup switch {
                "vertex" => BuildVertex(Part(parts, 1), Part(parts, 2)),
                "bedrock" => BuildBedrock(Part(parts, 1)),
                "azure" => Part(parts, 1),
                _ => parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
                    ? parts[1]
                    : preset.DefaultGateway ?? throw new ArgumentException("预设没有默认网关")
            };
        }

        public static string BuildVertex(string project, string location) {
            if (!Token.IsMatch(project ?? string.Empty)) {
                throw new ArgumentException("project ID 只能包含字母、数字、点、下划线和连字符");
            }
            if (!Region.IsMatch(location ?? string.Empty)) {
                throw new ArgumentException("location 只能包含小写字母、数字和连字符，例如 us-central1");
            }
            return $"https://{location}-aiplatform.googleapis.com/v1/projects/{project}/locations/{location}";
        }

        public static string BuildBedrock(string region) {
            if (!Region.IsMatch(region ?? string.Empty)) {
                throw new ArgumentException("region 只能包含小写字母、数字和连字符，例如 us-east-1");
            }
            return $"https://bedrock-runtime.{region}.amazonaws.com";
        }

        /// <summary>
        /// Accepts a resource name or a root URL on the Azure OpenAI hosts and returns <c>https://host/openai/v1</c>.
        /// </summary>
        public static string NormalizeAzure(string input) {
            var trimmed = (input ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(trimmed)) {
                throw new ArgumentException("请输入 Azure 资源名或资源根地址");
            }

            if (!trimmed.Contains("://", StringComparison.Ordinal)) {
                if (!Token.IsMatch(trimmed)) {
                    throw new ArgumentException("资源名只能包含字母、数字、点、下划线和连字符");
                }
                trimmed = $"https://{trimmed}.openai.azure.com";
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) {
                throw new ArgumentException("请输入有效的 Azure 资源根地址");
            }

            var host = uri.Host;
            var allowed = host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".ai.azure.com", StringComparison.OrdinalIgnoreCase);
            if (!allowed) {
                throw new ArgumentException("Azure 地址主机须为 openai.azure.com、cognitiveservices.azure.com 或 ai.azure.com");
            }

            return $"{uri.Scheme}://{host}/openai/v1";
        }

        private static string Part(string[] parts, int index) =>
            index < parts.Length ? parts[index] : string.Empty;
    }
}

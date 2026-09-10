using System;
using System.Net.Http;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// OpenCode Go requires a stable `x-opencode-session` per conversation for every request.
    /// https://opencode.ai/docs/go/#where-can-i-use-it
    /// </summary>
    internal static class OpencodeSessionHeaders {
        internal const string GlobalSessionId = "tsb-global";

        // ponytail: session id is derived from ChatId (stable per conversation); switch to real
        // session keys only if opencode ever requires stricter per-thread routing.
        internal static void Apply(HttpClient httpClient, string gateway, string sessionId = GlobalSessionId) {
            if (httpClient == null ||
                string.IsNullOrWhiteSpace(gateway) ||
                !gateway.Contains("opencode", StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            httpClient.DefaultRequestHeaders.Remove("x-opencode-session");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-opencode-session", sessionId);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TelegramSearchBot/1.0");
        }

        // 检测用实际请求 endpoint（binding 优先，回退 channel.Gateway）。
        internal static void Apply(HttpClient httpClient, LLMChannel channel, LLMApiBinding binding, string sessionId = GlobalSessionId)
            => Apply(httpClient, LlmBindingSupport.ResolveEndpoint(channel, binding), sessionId);
    }
}

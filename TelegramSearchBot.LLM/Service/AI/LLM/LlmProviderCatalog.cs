using System;
using System.Collections.Generic;
using System.Linq;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>A built-in provider preset: one code-defined entry of the provider catalog.</summary>
    public sealed record LlmProviderPreset(
        string Id,
        string DisplayName,
        LLMProvider Provider,
        /// <summary>Null = gateway must be entered during creation (e.g. user-owned OpenCode Go gateway).</summary>
        string? DefaultGateway,
        string[] DefaultModels,
        bool RequiresApiKey,
        string? Notes = null,
        /// <summary>Extra protocol bindings created alongside the channel default (multi-protocol gateways).</summary>
        IReadOnlyList<LlmPresetBinding>? Bindings = null,
        /// <summary>Model-name prefix → binding id; unmatched models stay on the channel default binding.</summary>
        IReadOnlyList<LlmModelBindingRule>? ModelBindingRules = null,
        /// <summary>
        /// True when the gateway catalog IS the entitlement (subscription, e.g. OpenCode Go): refresh may add
        /// discovered models. False for pay-per-token catalogs (e.g. OpenCode Zen) where listing ≠ authorization.
        /// </summary>
        bool CatalogIsEntitlement = false);

    /// <summary>Extra API binding a preset creates: same gateway, different wire protocol/auth.</summary>
    public sealed record LlmPresetBinding(
        string Id,
        LlmProtocol Protocol,
        LlmAuthProfile AuthProfile,
        /// <summary>Path appended to the entered gateway; not duplicated when the gateway already ends with it.</summary>
        string EndpointSuffix = "/v1");

    /// <summary>Model-name (case-insensitive) prefix → binding id.</summary>
    public sealed record LlmModelBindingRule(string BindingId, params string[] Prefixes);

    public static class LlmModelBindingRules {
        /// <summary>First matching rule wins; null = keep the channel default binding.</summary>
        public static string? ResolveBindingId(this IReadOnlyList<LlmModelBindingRule>? rules, string? modelName) {
            if (rules == null || string.IsNullOrWhiteSpace(modelName)) {
                return null;
            }

            foreach (var rule in rules) {
                if (rule.Prefixes.Any(prefix => modelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) {
                    return rule.BindingId;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Code-defined provider catalog (pi-style): common providers ship preconfigured so the bot
    /// admin only picks one and enters an API key. Custom endpoints are still supported by the
    /// existing manual channel flow (新建渠道) and LLMApiBinding overrides.
    /// </summary>
    public static class LlmProviderCatalog {
        public static readonly IReadOnlyList<LlmProviderPreset> Presets = new[] {
            new LlmProviderPreset(
                "anthropic", "Anthropic 官方", LLMProvider.Anthropic,
                "https://api.anthropic.com",
                new[] { "claude-sonnet-4-5", "claude-opus-4-1", "claude-haiku-4-5" },
                RequiresApiKey: true),
            new LlmProviderPreset(
                "openai", "OpenAI 官方 (Chat Completions)", LLMProvider.OpenAI,
                "https://api.openai.com/v1",
                new[] { "gpt-4o", "gpt-4o-mini", "gpt-4.1" },
                RequiresApiKey: true),
            new LlmProviderPreset(
                "openai-responses", "OpenAI 官方 (Responses API)", LLMProvider.ResponsesAPI,
                "https://api.openai.com/v1",
                new[] { "gpt-4o", "gpt-4.1" },
                RequiresApiKey: true),
            new LlmProviderPreset(
                "gemini", "Google Gemini", LLMProvider.Gemini,
                "https://generativelanguage.googleapis.com",
                new[] { "gemini-2.0-flash", "gemini-2.5-pro" },
                RequiresApiKey: true),
            new LlmProviderPreset(
                "minimax", "MiniMax", LLMProvider.MiniMax,
                "https://api.minimax.chat/v1",
                new[] { "MiniMax-Text-01", "abab6.5s-chat" },
                RequiresApiKey: true),
            new LlmProviderPreset(
                "ollama", "本地 Ollama", LLMProvider.Ollama,
                "http://localhost:11434",
                Array.Empty<string>(),
                RequiresApiKey: false,
                Notes: "本地服务无需 API Key；模型通过 `添加模型` 手动添加或自动发现。"),
            new LlmProviderPreset(
                "lmstudio", "本地 LM Studio", LLMProvider.LMStudio,
                "http://localhost:1234/v1",
                Array.Empty<string>(),
                RequiresApiKey: false,
                Notes: "本地服务无需 API Key；模型通过 `添加模型` 手动添加。"),
            new LlmProviderPreset(
                "deepseek", "DeepSeek (OpenAI 兼容)", LLMProvider.OpenAI,
                "https://api.deepseek.com/v1",
                new[] { "deepseek-chat", "deepseek-reasoner" },
                RequiresApiKey: true),
            new LlmProviderPreset(
                "moonshot", "Moonshot Kimi (OpenAI 兼容)", LLMProvider.OpenAI,
                "https://api.moonshot.cn/v1",
                new[] { "kimi-k2-0711-preview", "moonshot-v1-128k" },
                RequiresApiKey: true),
            // OpenCode 网关不是单协议服务商：Zen 按模型分属 Anthropic/OpenAI/Google 协议，
            // Go 只有 Responses + Chat Completions。预设建渠道时按 ModelBindingRules 拆 binding。
            new LlmProviderPreset(
                "opencode-zen", "OpenCode Zen (官方订阅目录)", LLMProvider.OpenAI,
                "https://opencode.ai/zen/v1",
                Array.Empty<string>(),
                RequiresApiKey: true,
                Notes: "多协议网关（claude-*→Anthropic、gpt-*/grok-*→Responses、gemini-*→Google、其余→Chat Completions）；按量计费，目录不自动创建，授权模型请用 `添加模型`。",
                Bindings: new[] {
                    new LlmPresetBinding("anthropic", LlmProtocol.AnthropicMessages, LlmAuthProfile.AnthropicApiKey),
                    new LlmPresetBinding("responses", LlmProtocol.OpenAIResponses, LlmAuthProfile.Bearer),
                    new LlmPresetBinding("google", LlmProtocol.Gemini, LlmAuthProfile.Bearer)
                },
                ModelBindingRules: new[] {
                    new LlmModelBindingRule("anthropic", "claude-"),
                    new LlmModelBindingRule("responses", "gpt-", "grok-", "o3", "o4"),
                    new LlmModelBindingRule("google", "gemini-")
                }),
            new LlmProviderPreset(
                "opencode-go", "OpenCode Go (订阅网关)", LLMProvider.OpenAI,
                "https://opencode.ai/zen/go/v1",
                new[] {
                    "grok-4.6", "gpt-5.6-luna",
                    "glm-5.3-flash", "glm-5.3", "glm-5.2", "glm-5.1",
                    "kimi-k3", "kimi-k2.7-code", "kimi-k2.6",
                    "longcat-2.0",
                    "deepseek-v4.1-flash", "deepseek-v4-pro", "deepseek-v4-flash", "deepseek-v4-flash-vision-exp",
                    "minimax-m3", "minimax-m2.7",
                    "mimo-v2.5", "mimo-v2.5-pro",
                    "qwen3.8-max", "qwen3.8-flash", "qwen3.7-max", "qwen3.7-plus", "qwen3.6-plus",
                    "muse-spark-1.3-contributor", "muse-spark-1.2-contributor",
                    "hy4-preview", "hy3"
                },
                RequiresApiKey: true,
                Notes: "OpenAI 兼容订阅网关（/responses + /chat/completions），自动携带 x-opencode-session；订阅覆盖目录模型，刷新会同步目录。自建网关请用 `新建渠道`。",
                Bindings: new[] {
                    new LlmPresetBinding("responses", LlmProtocol.OpenAIResponses, LlmAuthProfile.Bearer)
                },
                ModelBindingRules: new[] {
                    new LlmModelBindingRule("responses", "grok-", "gpt-")
                },
                CatalogIsEntitlement: true),
        };

        public static LlmProviderPreset? FindById(string id) =>
            Presets.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 按渠道端点识别 OpenCode 预设（opencode.ai/zen/go/* → Go，其余 /zen/* → Zen）。
        /// 用于刷新时决定目录策略与模型→协议规则。
        /// </summary>
        public static LlmProviderPreset? FindForEndpoint(string? endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint) ||
                !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Host, "opencode.ai", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            var path = uri.AbsolutePath;
            if (path.StartsWith("/zen/go", StringComparison.OrdinalIgnoreCase)) {
                return FindById("opencode-go");
            }
            if (path.Equals("/zen", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/zen/", StringComparison.OrdinalIgnoreCase)) {
                return FindById("opencode-zen");
            }
            return null;
        }

        /// <summary>把用户输入的网关补成带协议路径后缀的 binding 端点（后缀已存在时不重复追加）。</summary>
        public static string BuildBindingEndpoint(string? gateway, string? suffix) {
            var trimmed = (gateway ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrEmpty(suffix)) {
                return trimmed;
            }
            return trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + suffix;
        }

        /// <summary>Human-readable numbered list for bot selection.</summary>
        public static string FormatMenu() {
            var sb = new System.Text.StringBuilder("请选择要创建的渠道预设：\n");
            for (var i = 0; i < Presets.Count; i++) {
                var p = Presets[i];
                sb.AppendLine($"{i + 1}. {p.DisplayName}  ({p.DefaultGateway ?? "创建时输入"})");
                if (p.DefaultModels.Length > 0) {
                    sb.AppendLine($"   默认模型: {string.Join(", ", p.DefaultModels)}");
                }
                if (!string.IsNullOrEmpty(p.Notes)) {
                    sb.AppendLine($"   备注: {p.Notes}");
                }
            }

            sb.Append("\n发送编号选择；发送 取消 退出。");
            return sb.ToString();
        }
    }
}

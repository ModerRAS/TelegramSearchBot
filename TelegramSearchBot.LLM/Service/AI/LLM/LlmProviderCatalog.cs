using System;
using System.Collections.Generic;
using System.Linq;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>A built-in provider preset: one entry of the provider catalog.</summary>
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
    /// Provider catalog (pi-style): presets ship as data (`Providers/providers.json`, embedded) so providers and
    /// model lists can be updated without code changes — a user override at
    /// `%LOCALAPPDATA%/TelegramSearchBot/providers.json` wins over the built-in copy.
    /// Custom endpoints are still supported by the manual channel flow (新建渠道) and LLMApiBinding overrides.
    /// </summary>
    public static class LlmProviderCatalog {
        private static readonly Lazy<LlmProviderCatalogDocument> Document =
            new(() => LlmProviderCatalogLoader.Load(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static IReadOnlyList<LlmProviderPreset> Presets => Document.Value.Presets;

        /// <summary>内置/覆盖目录的数据版本（providers.json 的 generatedAt）。</summary>
        public static DateTimeOffset? GeneratedAt => Document.Value.GeneratedAt;

        /// <summary>目录来源（`builtin` 或 `file:<path>`），便于排障。</summary>
        public static string Source => Document.Value.Source;

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

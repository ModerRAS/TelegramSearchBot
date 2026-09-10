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
        string? Notes = null);

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
            new LlmProviderPreset(
                "opencode-zen", "OpenCode Zen (官方订阅目录)", LLMProvider.Anthropic,
                "https://opencode.ai/zen",
                Array.Empty<string>(),
                RequiresApiKey: true,
                Notes: "Anthropic 兼容；订阅 token 作为 API Key；目录模型不自动创建，授权模型请用 添加模型 手工维护。"),
            new LlmProviderPreset(
                "opencode-go", "OpenCode Go (自建网关)", LLMProvider.Anthropic,
                null,
                Array.Empty<string>(),
                RequiresApiKey: true,
                Notes: "创建时需输入你的 OpenCode Go 网关地址；Anthropic 兼容，自动携带 x-opencode-session 头；模型请用 添加模型 维护。"),
        };

        public static LlmProviderPreset? FindById(string id) =>
            Presets.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

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

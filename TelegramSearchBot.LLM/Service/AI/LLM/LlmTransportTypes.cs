using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>Transport identity + credentials + quirks, resolved from LLMChannel + LLMApiBinding.</summary>
    public sealed class LlmTransportConfig {
        public string ModelName { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string? ApiKey { get; set; }
        public LLMProvider Provider { get; set; }
        public LlmProtocol? Protocol { get; set; }
        public LLMApiBinding? Binding { get; set; }
        public LLMChannel? Channel { get; set; }
        /// <summary>Text-embedded (XML) tool protocol instead of native tool calling.</summary>
        public bool UseTextToolProtocol { get; set; }
        /// <summary>Whether vision inputs may be sent (from model capabilities).</summary>
        public bool SupportsVision { get; set; }
        /// <summary>OpenAI-family prompt caching enabled (provider-specific).</summary>
        public bool PromptCachingEnabled { get; set; }
        /// <summary>OpenAI reasoning_content passthrough quirk (Kimi/DeepSeek thinking models).</summary>
        public bool IncludeEmptyReasoningContent { get; set; }
    }

    /// <summary>One transport turn request: normalized history + tools + config.</summary>
    public sealed class LlmTurnRequest {
        public string SystemPrompt { get; set; } = string.Empty;
        public IReadOnlyList<LlmMessage> History { get; set; } = [];
        public IReadOnlyList<LlmToolSpec>? Tools { get; set; }
        public LlmTransportConfig Config { get; set; } = new();
    }

    /// <summary>
    /// Stateless per-turn transport adapter for one API dialect. Converts the normalized
    /// history/tools/config into a provider request, streams one assistant turn, and maps
    /// deltas to <see cref="LlmStreamEvent"/>. One instance per execution run (may hold
    /// provider clients and prompt-caching state).
    /// </summary>
    public interface ILlmTransport {
        bool SupportsNativeTools { get; }
        IAsyncEnumerable<LlmStreamEvent> StreamTurnAsync(LlmTurnRequest request, CancellationToken cancellationToken = default);
    }
}

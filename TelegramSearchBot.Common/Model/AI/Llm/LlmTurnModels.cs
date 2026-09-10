using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TelegramSearchBot.Model.AI {
    /// <summary>
    /// Normalized native tool call surfaced by a transport (native tool-calling protocol).
    /// Arguments are raw JSON; each transport parses/normalizes them as needed.
    /// </summary>
    public sealed class LlmNativeToolCall {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ArgumentsJson { get; set; } = "{}";

        public Dictionary<string, string> ParseArguments() {
            if (string.IsNullOrWhiteSpace(ArgumentsJson)) {
                return new Dictionary<string, string>();
            }
            try {
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ArgumentsJson)
                       ?.ToDictionary(kv => kv.Key, kv => kv.Value.ToString())
                       ?? new Dictionary<string, string>();
            } catch (Exception) {
                return new Dictionary<string, string>();
            }
        }
    }

    /// <summary>Result of executing one tool call, keyed by native call id.</summary>
    public sealed class LlmToolResult {
        public string ToolCallId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public bool IsError { get; set; }
    }

    /// <summary>Outcome of one assistant turn (one LLM request), after streaming completed.</summary>
    public sealed class LlmTurnResult {
        /// <summary>Full turn text (not trimmed).</summary>
        public string Text { get; set; } = string.Empty;
        /// <summary>Reasoning/thinking content, when the provider streams it.</summary>
        public string Reasoning { get; set; } = string.Empty;
        /// <summary>Native tool calls, empty for text-protocol sources and pure-text turns.</summary>
        public List<LlmNativeToolCall> ToolCalls { get; set; } = new();
        /// <summary>
        /// Native protocol: tool call metadata was malformed and the source already appended a
        /// self-correction message to its history; the loop must continue without executing tools.
        /// </summary>
        public bool MalformedToolCall { get; set; }
        /// <summary>Provider-specific usage observation for prompt-caching telemetry (opaque).</summary>
        public object? UsageObservation { get; set; }
        /// <summary>True when at least one delta arrived (text or thinking) during the turn.</summary>
        public bool StreamedAny { get; set; }
    }

    /// <summary>Events streamed by a turn source. Always terminates with TurnCompleted.</summary>
    public abstract record LlmStreamEvent {
        public sealed record TextDelta(string Delta) : LlmStreamEvent;
        public sealed record ThinkingDelta(string Delta) : LlmStreamEvent;
        public sealed record TurnCompleted(LlmTurnResult Turn) : LlmStreamEvent;
    }
}

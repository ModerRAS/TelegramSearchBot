using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TelegramSearchBot.Model.AI {
    /// <summary>Normalized message role across all API dialects.</summary>
    public enum LlmRole {
        System = 0,
        User = 1,
        Assistant = 2,
        Tool = 3
    }

    /// <summary>A normalized tool call initiated by an assistant message.</summary>
    public sealed class LlmToolCall {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>Arguments as string dict (the canonical form used by McpToolHelper).</summary>
        public Dictionary<string, string> ParseArguments() {
            if (string.IsNullOrWhiteSpace(ArgumentsJson)) {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            try {
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ArgumentsJson)
                       ?.ToDictionary(kv => kv.Key, kv => kv.Value.ToString())
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            } catch (Exception) {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Tool result appended after a tool call (mirrors assistant tool_calls by id).</summary>
    public sealed class LlmToolResult {
        public string ToolCallId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public bool IsError { get; set; }
    }

    /// <summary>
    /// The single normalized message representation used end to end: history projection,
    /// agent loop state, transport requests, and continuation snapshots (schema v2).
    /// Replaces per-provider history types and their serialize/deserialize pairs.
    /// </summary>
    public sealed class LlmMessage {
        public LlmRole Role { get; set; }
        /// <summary>Text content. For Tool-role messages this is the tool result text.</summary>
        public string? Text { get; set; }
        /// <summary>Reasoning/thinking content for assistant messages (thinking-mode models).</summary>
        public string? Thinking { get; set; }
        /// <summary>Tool calls initiated by this assistant message (null otherwise).</summary>
        public List<LlmToolCall>? ToolCalls { get; set; }
        /// <summary>Tool call id for Tool-role messages.</summary>
        public string? ToolCallId { get; set; }
        /// <summary>PNG image bytes for user visual input (vision-capable transports only).</summary>
        public byte[]? ImagePng { get; set; }
        public string? ImageMediaType { get; set; }
        /// <summary>Tool-role only: whether the tool execution failed.</summary>
        public bool IsToolError { get; set; }

        public static LlmMessage System(string text) => new() { Role = LlmRole.System, Text = text };
        public static LlmMessage User(string text, byte[]? imagePng = null, string? imageMediaType = null)
            => new() { Role = LlmRole.User, Text = text, ImagePng = imagePng, ImageMediaType = imageMediaType };
        public static LlmMessage Assistant(string? text, string? thinking = null, List<LlmToolCall>? toolCalls = null)
            => new() { Role = LlmRole.Assistant, Text = text, Thinking = thinking, ToolCalls = toolCalls };
        public static LlmMessage ToolResult(string toolCallId, string name, string result, bool isError = false)
            => new() { Role = LlmRole.Tool, ToolCallId = toolCallId, Thinking = name, Text = result, IsToolError = isError };
    }

    /// <summary>Normalized tool definition advertised to the model.</summary>
    public sealed class LlmToolSpec {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        /// <summary>JSON schema for parameters (provider-neutral).</summary>
        public string ParametersJson { get; set; } = "{}";
        public bool StrictSchema { get; set; }
    }

    /// <summary>Result of one assistant turn after streaming completed.</summary>
    public sealed class LlmTurnResult {
        /// <summary>Full turn text (not trimmed).</summary>
        public string Text { get; set; } = string.Empty;
        /// <summary>Reasoning/thinking content, when the provider streams it.</summary>
        public string Reasoning { get; set; } = string.Empty;
        /// <summary>Native tool calls, empty for text-protocol sources and pure-text turns.</summary>
        public List<LlmToolCall> ToolCalls { get; set; } = new();
        /// <summary>
        /// Native protocol: tool call metadata was malformed and the transport already appended a
        /// self-correction message to its history; the loop must continue without executing tools.
        /// </summary>
        public bool MalformedToolCall { get; set; }
        /// <summary>Provider-specific usage observation for prompt-caching telemetry (opaque).</summary>
        public object? UsageObservation { get; set; }
        /// <summary>True when at least one delta arrived during the turn.</summary>
        public bool StreamedAny { get; set; }
    }
}

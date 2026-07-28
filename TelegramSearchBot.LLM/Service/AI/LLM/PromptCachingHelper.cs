#pragma warning disable OPENAI001
#pragma warning disable SCME0001

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI.Chat;
using OpenAI.Responses;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    public static class PromptCachingOutcomes {
        public const string Disabled = "disabled";
        public const string Unsupported = "unsupported";
        public const string AttemptedWrite = "attempted_write";
        public const string Hit = "hit";
        public const string Miss = "miss";
        public const string NoSignal = "no_signal";
        public const string Error = "error";
    }

    public sealed class PromptCachingObservation {
        public string Provider { get; set; } = string.Empty;
        public int ChannelId { get; set; }
        public string Model { get; set; } = string.Empty;
        public bool PromptCachingEnabled { get; set; }
        public string StablePrefixHash { get; set; } = string.Empty;
        public string ToolDefinitionHash { get; set; } = string.Empty;
        public string CacheOutcome { get; set; } = PromptCachingOutcomes.NoSignal;
        public string MissReason { get; set; }
        public string PromptCacheKey { get; set; }
        public string PromptCacheRetention { get; set; }
        public bool CacheKeyAttached { get; set; }
        public bool CacheBreakpointInserted { get; set; }
        public int? CachedTokenCount { get; set; }
        public long? CacheCreationInputTokens { get; set; }
        public long? CacheReadInputTokens { get; set; }
        public string ProviderUsageJson { get; set; }
    }

    public static class PromptCachingHelper {
        public const string OpenAiDefaultPromptCacheRetention = "in_memory";

        private static readonly JsonSerializerSettings CanonicalJsonSettings = new() {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
        };

        private static readonly ConcurrentDictionary<string, byte> SeenCacheRoutingKeys = new(StringComparer.Ordinal);

        public static string ComputeToolDefinitionHash() {
            return ComputeCanonicalHash(McpToolHelper.ExportToolDefinitions());
        }

        public static string ComputeStablePrefixHash(object payload) {
            return ComputeCanonicalHash(payload);
        }

        public static string BuildOpenAiPromptCacheKey(string provider, string model, string toolDefinitionHash, string stablePrefixHash) {
            return string.Join(":", new[] {
                SanitizeKeySegment(provider),
                SanitizeKeySegment(model),
                toolDefinitionHash,
                stablePrefixHash,
            });
        }

        public static void ApplyOpenAiPromptCaching(ChatCompletionOptions options, string promptCacheKey, string promptCacheRetention) {
            ref var patch = ref options.Patch;
            patch.Set("$.prompt_cache_key"u8, promptCacheKey);
            if (!string.IsNullOrWhiteSpace(promptCacheRetention)) {
                patch.Set("$.prompt_cache_retention"u8, promptCacheRetention);
            }
        }

        public static void ApplyOpenAiPromptCaching(CreateResponseOptions options, string promptCacheKey, string promptCacheRetention) {
            ref var patch = ref options.Patch;
            patch.Set("$.prompt_cache_key"u8, promptCacheKey);
            if (!string.IsNullOrWhiteSpace(promptCacheRetention)) {
                patch.Set("$.prompt_cache_retention"u8, promptCacheRetention);
            }
        }

        public static string DetermineOpenAiOutcome(bool promptCachingEnabled, bool cacheKeyAttached, string promptCacheKey, int? cachedTokenCount, out string missReason) {
            missReason = null;
            if (!promptCachingEnabled) {
                return PromptCachingOutcomes.Disabled;
            }

            if (!cacheKeyAttached || string.IsNullOrWhiteSpace(promptCacheKey)) {
                missReason = "prompt_cache_key_not_attached";
                return PromptCachingOutcomes.Unsupported;
            }

            if (!cachedTokenCount.HasValue) {
                missReason = "cached_tokens_unavailable";
                return PromptCachingOutcomes.NoSignal;
            }

            if (cachedTokenCount.Value > 0) {
                SeenCacheRoutingKeys[promptCacheKey] = 0;
                return PromptCachingOutcomes.Hit;
            }

            var firstObservation = SeenCacheRoutingKeys.TryAdd(promptCacheKey, 0);
            missReason = firstObservation
                ? "first_observation_without_cached_tokens"
                : "repeated_observation_without_cached_tokens";
            return firstObservation
                ? PromptCachingOutcomes.AttemptedWrite
                : PromptCachingOutcomes.Miss;
        }

        public static string DetermineAnthropicOutcome(bool promptCachingEnabled, bool cacheBreakpointInserted, string observationKey, long? cacheCreationInputTokens, long? cacheReadInputTokens, out string missReason) {
            missReason = null;
            if (!promptCachingEnabled) {
                return PromptCachingOutcomes.Disabled;
            }

            if (!cacheBreakpointInserted) {
                missReason = "cache_control_not_inserted";
                return PromptCachingOutcomes.Unsupported;
            }

            if (cacheReadInputTokens.GetValueOrDefault() > 0) {
                SeenCacheRoutingKeys[observationKey] = 0;
                return PromptCachingOutcomes.Hit;
            }

            if (cacheCreationInputTokens.GetValueOrDefault() > 0) {
                SeenCacheRoutingKeys[observationKey] = 0;
                return PromptCachingOutcomes.AttemptedWrite;
            }

            var firstObservation = SeenCacheRoutingKeys.TryAdd(observationKey, 0);
            missReason = firstObservation
                ? "usage_reported_zero_cache_tokens"
                : "repeated_prefix_without_cache_read";
            return firstObservation
                ? PromptCachingOutcomes.NoSignal
                : PromptCachingOutcomes.Miss;
        }

        public static void LogObservation(ILogger logger, PromptCachingObservation observation) {
            logger.LogInformation(
                "Prompt caching observation. Provider={Provider}, ChannelId={ChannelId}, Model={Model}, PromptCachingEnabled={PromptCachingEnabled}, StablePrefixHash={StablePrefixHash}, ToolDefinitionHash={ToolDefinitionHash}, CacheOutcome={CacheOutcome}, MissReason={MissReason}, PromptCacheKey={PromptCacheKey}, PromptCacheRetention={PromptCacheRetention}, CacheKeyAttached={CacheKeyAttached}, CacheBreakpointInserted={CacheBreakpointInserted}, CachedTokenCount={CachedTokenCount}, CacheCreationInputTokens={CacheCreationInputTokens}, CacheReadInputTokens={CacheReadInputTokens}, ProviderUsage={ProviderUsage}",
                observation.Provider,
                observation.ChannelId,
                observation.Model,
                observation.PromptCachingEnabled,
                observation.StablePrefixHash,
                observation.ToolDefinitionHash,
                observation.CacheOutcome,
                observation.MissReason,
                observation.PromptCacheKey,
                observation.PromptCacheRetention,
                observation.CacheKeyAttached,
                observation.CacheBreakpointInserted,
                observation.CachedTokenCount,
                observation.CacheCreationInputTokens,
                observation.CacheReadInputTokens,
                observation.ProviderUsageJson);
        }

        private static string ComputeCanonicalHash(object payload) {
            var json = JsonConvert.SerializeObject(payload, CanonicalJsonSettings);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string SanitizeKeySegment(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return "unknown";
            }

            var builder = new StringBuilder(value.Length);
            foreach (var c in value.Trim()) {
                builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
            }
            return builder.ToString();
        }
    }
}

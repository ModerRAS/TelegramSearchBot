using System;
using System.Linq;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class LlmProviderCatalogTests {
        [Theory]
        [InlineData("claude-sonnet-5", "anthropic")]
        [InlineData("Claude-Opus-5", "anthropic")]
        [InlineData("gpt-5.6-luna", "responses")]
        [InlineData("grok-4.6", "responses")]
        [InlineData("gemini-3.8-flash", "google")]
        public void OpenCodeZen_RulesMapModelsToProtocols(string modelName, string expectedBindingId) {
            var zen = LlmProviderCatalog.FindById("opencode-zen")!;

            Assert.Equal(expectedBindingId, zen.ModelBindingRules.ResolveBindingId(modelName));
        }

        [Theory]
        [InlineData("glm-5.3", null)]
        [InlineData("kimi-k3", null)]
        [InlineData("deepseek-v4-pro", null)]
        public void OpenCodeZen_UnmatchedModelsKeepDefaultBinding(string modelName, string? expectedBindingId) {
            var zen = LlmProviderCatalog.FindById("opencode-zen")!;

            Assert.Equal(expectedBindingId, zen.ModelBindingRules.ResolveBindingId(modelName));
        }

        [Theory]
        [InlineData("grok-4.6", "responses")]
        [InlineData("gpt-5.6-luna", "responses")]
        [InlineData("glm-5.3", null)]
        public void OpenCodeGo_RulesMapResponsesModelsOnly(string modelName, string? expectedBindingId) {
            var go = LlmProviderCatalog.FindById("opencode-go")!;

            Assert.Equal(expectedBindingId, go.ModelBindingRules.ResolveBindingId(modelName));
        }

        [Fact]
        public void OpenCodeGo_SeedsOfficialModels_WithResponsesBindingForGrokAndGpt() {
            var go = LlmProviderCatalog.FindById("opencode-go")!;

            Assert.Contains("grok-4.6", go.DefaultModels);
            Assert.Contains("minimax-m3", go.DefaultModels);
            Assert.Contains("qwen3.7-max", go.DefaultModels);
            Assert.Equal("responses", go.ModelBindingRules.ResolveBindingId("grok-4.6"));
            Assert.Equal("responses", go.ModelBindingRules.ResolveBindingId("gpt-5.6-luna"));
            Assert.Null(go.ModelBindingRules.ResolveBindingId("glm-5.3"));
            Assert.True(go.CatalogIsEntitlement);
        }

        [Fact]
        public void OpenCodeZen_IsNotEntitlement_AndSeedsNoModels() {
            var zen = LlmProviderCatalog.FindById("opencode-zen")!;

            Assert.False(zen.CatalogIsEntitlement);
            Assert.Empty(zen.DefaultModels);
            Assert.Equal("https://opencode.ai/zen/v1", zen.DefaultGateway);
            Assert.Equal(3, zen.Bindings!.Count);
        }

        [Theory]
        [InlineData("https://opencode.ai/zen/go/v1", "opencode-go")]
        [InlineData("https://opencode.ai/zen/go", "opencode-go")]
        [InlineData("https://opencode.ai/zen/v1", "opencode-zen")]
        [InlineData("https://opencode.ai/zen", "opencode-zen")]
        [InlineData("https://api.openai.com/v1", null)]
        [InlineData("not a url", null)]
        public void FindForEndpoint_ClassifiesOpenCodeGateways(string endpoint, string? expectedPresetId) {
            Assert.Equal(expectedPresetId, LlmProviderCatalog.FindForEndpoint(endpoint)?.Id);
        }

        [Theory]
        [InlineData("https://opencode.ai/zen", "/v1", "https://opencode.ai/zen/v1")]
        [InlineData("https://opencode.ai/zen/v1", "/v1", "https://opencode.ai/zen/v1")]
        [InlineData("https://opencode.ai/zen/v1/", "/v1", "https://opencode.ai/zen/v1")]
        [InlineData("https://example.com", null, "https://example.com")]
        public void BuildBindingEndpoint_AppendsSuffixAtMostOnce(string gateway, string? suffix, string expected) {
            Assert.Equal(expected, LlmProviderCatalog.BuildBindingEndpoint(gateway, suffix));
        }

        [Fact]
        public void EveryPreset_HasResolvableRuleBindings() {
            foreach (var preset in LlmProviderCatalog.Presets) {
                if (preset.ModelBindingRules == null) {
                    continue;
                }

                var bindingIds = preset.Bindings?.Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                                 ?? new System.Collections.Generic.HashSet<string>();
                foreach (var rule in preset.ModelBindingRules) {
                    // 规则引用的 binding 必须存在于预设声明中（默认 binding 由渠道提供，不在此列）。
                    Assert.Contains(rule.BindingId, bindingIds);
                }
            }
        }
    }
}

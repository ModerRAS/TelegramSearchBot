using System;
using System.IO;
using System.Linq;
using System.Text;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class LlmProviderCatalogLoaderTests {
        private static string TempPath(string name) {
            var dir = Path.Combine(Path.GetTempPath(), "tsb-providers-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }

        [Fact]
        public void BuiltInDocument_LoadsAllPresets_WhenOverrideMissing() {
            var missing = TempPath("providers.json");

            var doc = LlmProviderCatalogLoader.Load(overridePath: missing);

            Assert.Equal("builtin", doc.Source);
            Assert.Contains("不存在", doc.Warning);
            Assert.Equal(11, doc.Presets.Count);
            Assert.NotNull(doc.GeneratedAt);
            Assert.NotNull(doc.Presets.FirstOrDefault(p => p.Id == "opencode-go"));
        }

        [Fact]
        public void BuiltInDocument_KeepsOpenCodeBindingsAndRules() {
            var go = LlmProviderCatalog.FindById("opencode-go")!;

            Assert.Single(go.Bindings!);
            Assert.Equal(LlmProtocol.OpenAIResponses, go.Bindings![0].Protocol);
            Assert.Equal("responses", go.ModelBindingRules.ResolveBindingId("grok-4.6"));
            Assert.True(go.CatalogIsEntitlement);
            Assert.Equal("https://opencode.ai/zen/go/v1", go.DefaultGateway);
        }

        [Fact]
        public void OverrideFile_WinsOverBuiltIn() {
            var path = TempPath("providers.json");
            File.WriteAllText(path, """
            {
              "generatedAt": "2030-01-01T00:00:00Z",
              "presets": [
                { "id": "custom", "displayName": "Custom", "provider": "OpenAI", "defaultGateway": "https://example.com/v1",
                  "defaultModels": ["m1"], "requiresApiKey": true,
                  "bindings": [ { "id": "responses", "protocol": "OpenAIResponses", "authProfile": "Bearer" } ],
                  "modelBindingRules": [ { "bindingId": "responses", "prefixes": ["gpt-"] } ] }
              ]
            }
            """);

            var doc = LlmProviderCatalogLoader.Load(overridePath: path);

            Assert.StartsWith("file:", doc.Source);
            var preset = Assert.Single(doc.Presets);
            Assert.Equal("custom", preset.Id);
            Assert.Equal("responses", preset.ModelBindingRules.ResolveBindingId("gpt-x"));
            Assert.Equal(LlmProtocol.OpenAIResponses, preset.Bindings![0].Protocol);
            Assert.Equal(LlmAuthProfile.Bearer, preset.Bindings![0].AuthProfile);
        }

        [Fact]
        public void InvalidOverride_FallsBackToBuiltInStream_WithWarning() {
            var path = TempPath("providers.json");
            File.WriteAllText(path, "{ this is not valid json");

            using var builtIn = new MemoryStream(Encoding.UTF8.GetBytes("""
            { "generatedAt": "2026-01-01T00:00:00Z", "presets": [ { "id": "fallback", "displayName": "Fallback", "provider": "OpenAI", "defaultGateway": null, "defaultModels": [], "requiresApiKey": false } ] }
            """));

            var doc = LlmProviderCatalogLoader.Load(overridePath: path, builtIn: builtIn);

            Assert.Equal("builtin", doc.Source);
            Assert.Contains("回退", doc.Warning);
            Assert.Equal("fallback", Assert.Single(doc.Presets).Id);
        }

        [Fact]
        public void EmptyPresetList_InOverride_IsRejected() {
            var path = TempPath("providers.json");
            File.WriteAllText(path, """{ "presets": [] }""");
            using var builtIn = new MemoryStream(Encoding.UTF8.GetBytes("""
            { "presets": [ { "id": "fallback", "displayName": "Fallback", "provider": "OpenAI", "defaultGateway": null, "defaultModels": [], "requiresApiKey": false } ] }
            """));

            var doc = LlmProviderCatalogLoader.Load(overridePath: path, builtIn: builtIn);

            Assert.Equal("fallback", Assert.Single(doc.Presets).Id);
            Assert.NotNull(doc.Warning);
        }
    }
}

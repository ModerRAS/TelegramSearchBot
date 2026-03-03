using TelegramSearchBot.Model.AI;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    public class ModelWithCapabilitiesTests {
        [Fact]
        public void SupportsToolCalling_FunctionCalling_ReturnsTrue() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("function_calling", true);
            Assert.True(model.SupportsToolCalling);
        }

        [Fact]
        public void SupportsToolCalling_ToolCalls_ReturnsTrue() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("tool_calls", true);
            Assert.True(model.SupportsToolCalling);
        }

        [Fact]
        public void SupportsToolCalling_NeitherSet_ReturnsFalse() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            Assert.False(model.SupportsToolCalling);
        }

        [Fact]
        public void SupportsVision_VisionSet_ReturnsTrue() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("vision", true);
            Assert.True(model.SupportsVision);
        }

        [Fact]
        public void SupportsVision_ImageContent_ReturnsTrue() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("image_content", true);
            Assert.True(model.SupportsVision);
        }

        [Fact]
        public void SupportsVision_Multimodal_ReturnsTrue() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("multimodal", true);
            Assert.True(model.SupportsVision);
        }

        [Fact]
        public void IsEmbeddingModel_EmbeddingCapability_ReturnsTrue() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("embedding", true);
            Assert.True(model.IsEmbeddingModel);
        }

        [Fact]
        public void IsEmbeddingModel_NameContainsEmbedding_ReturnsTrue() {
            var model = new ModelWithCapabilities { ModelName = "text-embedding-3-small" };
            Assert.True(model.IsEmbeddingModel);
        }

        [Fact]
        public void IsEmbeddingModel_NoCapability_ReturnsFalse() {
            var model = new ModelWithCapabilities { ModelName = "gpt-4" };
            Assert.False(model.IsEmbeddingModel);
        }

        [Fact]
        public void SupportsStreaming_Set_ReturnsTrue() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("streaming", true);
            Assert.True(model.SupportsStreaming);
        }

        [Fact]
        public void SupportsEmbedding_TextEmbedding_ReturnsTrue() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("text_embedding", true);
            Assert.True(model.SupportsEmbedding);
        }

        [Fact]
        public void GetCapabilityBool_InvalidValue_ReturnsFalse() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("function_calling", "not_a_bool");
            Assert.False(model.GetCapabilityBool("function_calling"));
        }

        [Fact]
        public void GetCapabilityBool_MissingCapability_ReturnsFalse() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            Assert.False(model.GetCapabilityBool("nonexistent"));
        }

        [Fact]
        public void GetCapability_ExistingKey_ReturnsValue() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("custom_key", "custom_value");
            Assert.Equal("custom_value", model.GetCapability("custom_key"));
        }

        [Fact]
        public void GetCapability_MissingKey_ReturnsNull() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            Assert.Null(model.GetCapability("missing"));
        }

        [Fact]
        public void SetCapability_Bool_StoresLowerCaseString() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            model.SetCapability("test_cap", true);
            Assert.Equal("true", model.GetCapability("test_cap"));
        }

        [Fact]
        public void Capabilities_DefaultsToEmptyDictionary() {
            var model = new ModelWithCapabilities { ModelName = "test" };
            Assert.NotNull(model.Capabilities);
            Assert.Empty(model.Capabilities);
        }
    }
}

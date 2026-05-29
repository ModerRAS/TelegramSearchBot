using System;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.Service.Tools {
    public class ImageGenerationToolServiceTests {
        [Theory]
        [InlineData("https://api.openai.com", "https://api.openai.com/v1/images/generations")]
        [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/images/generations")]
        [InlineData("https://example.test/custom/", "https://example.test/custom/v1/images/generations")]
        [InlineData("https://example.test/v1/images/generations", "https://example.test/v1/images/generations")]
        public void BuildImageGenerationEndpoint_NormalizesGateway(string gateway, string expected) {
            Assert.Equal(expected, ImageGenerationToolService.BuildImageGenerationEndpoint(gateway));
        }

        [Fact]
        public void BuildImageGenerationEndpoint_WhenGatewayEmpty_Throws() {
            Assert.Throws<ArgumentException>(() => ImageGenerationToolService.BuildImageGenerationEndpoint(" "));
        }
    }
}

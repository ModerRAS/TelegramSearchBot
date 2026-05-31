using System;
using System.Linq;
using Newtonsoft.Json.Linq;
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

        [Theory]
        [InlineData("https://api.minimaxi.com", "https://api.minimaxi.com/v1/image_generation")]
        [InlineData("https://api.minimaxi.com/v1", "https://api.minimaxi.com/v1/image_generation")]
        [InlineData("https://example.test/custom/", "https://example.test/custom/v1/image_generation")]
        [InlineData("https://example.test/v1/image_generation", "https://example.test/v1/image_generation")]
        [InlineData("https://example.test/custom/image_generation", "https://example.test/custom/image_generation")]
        public void BuildMiniMaxImageGenerationEndpoint_NormalizesGateway(string gateway, string expected) {
            Assert.Equal(expected, ImageGenerationToolService.BuildMiniMaxImageGenerationEndpoint(gateway));
        }

        [Fact]
        public void BuildMiniMaxImageGenerationEndpoint_WhenGatewayEmpty_Throws() {
            Assert.Throws<ArgumentException>(() => ImageGenerationToolService.BuildMiniMaxImageGenerationEndpoint(" "));
        }

        [Fact]
        public void BuildMiniMaxImageGenerationRequestBody_UsesAspectRatioAndStyle() {
            var body = ImageGenerationToolService.BuildMiniMaxImageGenerationRequestBody(
                "画一张水彩风格的城市夜景",
                "image-01-live",
                "1024x1024",
                2,
                "base64",
                "16:9",
                123,
                true,
                true,
                "水彩",
                0.7);

            Assert.Equal("image-01-live", Value<string>(body, "model"));
            Assert.Equal("画一张水彩风格的城市夜景", Value<string>(body, "prompt"));
            Assert.Equal(2, Value<int>(body, "n"));
            Assert.Equal("base64", Value<string>(body, "response_format"));
            Assert.Equal("16:9", Value<string>(body, "aspect_ratio"));
            Assert.Null(body["width"]);
            Assert.Null(body["height"]);
            Assert.Equal(123, Value<int>(body, "seed"));
            Assert.True(Value<bool>(body, "prompt_optimizer"));
            Assert.True(Value<bool>(body, "aigc_watermark"));
            Assert.Equal("水彩", NestedValue<string>(body, "style", "style_type"));
            Assert.Equal(0.7, NestedValue<double>(body, "style", "style_weight"));
        }

        [Fact]
        public void BuildMiniMaxImageGenerationRequestBody_UsesWidthHeightForImage01WhenAspectRatioMissing() {
            var body = ImageGenerationToolService.BuildMiniMaxImageGenerationRequestBody(
                "A minimal product render",
                "image-01",
                "1152x864",
                1,
                "url",
                null,
                null,
                false,
                false,
                null,
                null);

            Assert.Equal(1152, Value<int>(body, "width"));
            Assert.Equal(864, Value<int>(body, "height"));
            Assert.Null(body["aspect_ratio"]);
        }

        [Fact]
        public void BuildMiniMaxImageGenerationRequestBody_WhenImage01SizeInvalid_Throws() {
            Assert.Throws<ArgumentException>(() => ImageGenerationToolService.BuildMiniMaxImageGenerationRequestBody(
                "A large render",
                "image-01",
                "2560x1440",
                1,
                "url",
                null,
                null,
                false,
                false,
                null,
                null));
        }

        [Fact]
        public void EnsureMiniMaxResponseSucceeded_WhenStatusNonZero_Throws() {
            var ex = Assert.Throws<InvalidOperationException>(() => ImageGenerationToolService.EnsureMiniMaxResponseSucceeded(
                @"{""base_resp"":{""status_code"":1026,""status_msg"":""sensitive prompt""}}"));

            Assert.Contains("1026", ex.Message);
            Assert.Contains("sensitive prompt", ex.Message);
        }

        [Fact]
        public void ExtractImages_ReadsMiniMaxUrlAndBase64Arrays() {
            var images = ImageGenerationToolService.ExtractImages(
                @"{""data"":{""image_urls"":[""https://example.test/a.png""],""image_base64"":[""YWJj""]},""base_resp"":{""status_code"":0,""status_msg"":""success""}}");

            Assert.Equal(2, images.Count);
            Assert.Equal("https://example.test/a.png", images.First().Url);
            Assert.Equal("YWJj", images.Last().Base64Data);
        }

        private static T Value<T>(JObject obj, string key) {
            var token = obj[key];
            Assert.NotNull(token);
            return token!.ToObject<T>()!;
        }

        private static T NestedValue<T>(JObject obj, string parentKey, string key) {
            var parent = obj[parentKey] as JObject;
            Assert.NotNull(parent);
            return Value<T>(parent!, key);
        }
    }
}

using System;
using System.Net.Http;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class OpencodeSessionHeadersTests {
        private static HttpClient Client() => new HttpClient();

        [Theory]
        [InlineData("https://opencode.ai/zen/v1")]
        [InlineData("https://opencode.ai/zen/v1/")]
        [InlineData("https://www.opencode.ai/zen/v1")]
        public void Apply_Opengateways_AddsSessionAndUserAgent(string gateway) {
            using var client = Client();

            OpencodeSessionHeaders.Apply(client, gateway, "tsb-123");

            Assert.Equal("tsb-123", string.Join(",", client.DefaultRequestHeaders.GetValues("x-opencode-session")));
            Assert.Equal("TelegramSearchBot/1.0", string.Join(",", client.DefaultRequestHeaders.GetValues("User-Agent")));
        }

        [Theory]
        [InlineData("https://api.openai.com/v1")]
        [InlineData("https://api.minimaxi.com/v1")]
        [InlineData(null)]
        [InlineData("")]
        public void Apply_NonOpencodeGateways_AddsNothing(string gateway) {
            using var client = Client();

            OpencodeSessionHeaders.Apply(client, gateway, "tsb-123");

            Assert.False(client.DefaultRequestHeaders.Contains("x-opencode-session"));
        }

        [Fact]
        public void Apply_Twice_DoesNotDuplicateSessionHeader() {
            using var client = Client();

            OpencodeSessionHeaders.Apply(client, "https://opencode.ai/zen/v1", "tsb-1");
            OpencodeSessionHeaders.Apply(client, "https://opencode.ai/zen/v1", "tsb-1");

            Assert.Single(client.DefaultRequestHeaders.GetValues("x-opencode-session"));
        }
    }
}

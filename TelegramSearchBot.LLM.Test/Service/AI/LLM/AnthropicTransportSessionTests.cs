using System.Linq;
using System.Net.Http;
using TelegramSearchBot.Service.AI.LLM.Transports;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class AnthropicTransportSessionTests {
        [Fact]
        public void CreateSessionHttpClient_AppliesStableSessionForOpenCodeEndpoints() {
            using var client = AnthropicMessagesTransport.CreateSessionHttpClient("https://opencode.ai/zen/go/v1", 42);

            Assert.Equal("tsb-42", client.DefaultRequestHeaders.GetValues("x-opencode-session").Single());
        }

        [Fact]
        public void CreateSessionHttpClient_UsesGlobalSessionWithoutChatId() {
            using var client = AnthropicMessagesTransport.CreateSessionHttpClient("https://opencode.ai/zen/v1");

            Assert.Equal("tsb-global", client.DefaultRequestHeaders.GetValues("x-opencode-session").Single());
        }

        [Fact]
        public void CreateSessionHttpClient_LeavesOtherEndpointsUntouched() {
            using var client = AnthropicMessagesTransport.CreateSessionHttpClient("https://api.anthropic.com/v1", 42);

            Assert.False(client.DefaultRequestHeaders.Contains("x-opencode-session"));
        }
    }
}

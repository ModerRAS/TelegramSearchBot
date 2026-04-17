using System.Threading.Tasks;
using TelegramSearchBot.LLMAgent.Service;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class ToolExecutorTests {
        [Fact]
        public async Task EchoAsync_ReturnsInputText() {
            var executor = new ToolExecutor(null!, null!);

            var result = await executor.EchoAsync("hello");

            Assert.Equal("hello", result);
        }

        [Fact]
        public async Task CalculateAsync_EvaluatesExpression() {
            var executor = new ToolExecutor(null!, null!);

            var result = await executor.CalculateAsync("1 + 2 * 3");

            Assert.Equal("7", result);
        }
    }
}

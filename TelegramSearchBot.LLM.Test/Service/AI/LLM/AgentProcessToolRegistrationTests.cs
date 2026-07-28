using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.LLMAgent;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.LLM.Test.Service.AI.LLM {
    public class AgentProcessToolRegistrationTests {
        [Fact]
        public void BuildServices_RegistersResponsesApiExecutor() {
            using var provider = BuildAgentServices();

            Assert.NotNull(provider.GetService<OpenAIResponsesService>());
        }

        private static ServiceProvider BuildAgentServices() {
            var method = typeof(LLMAgentProgram).GetMethod("BuildServices", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var provider = method!.Invoke(null, new object[] { 43210 }) as ServiceProvider;
            Assert.NotNull(provider);
            return provider!;
        }
    }
}

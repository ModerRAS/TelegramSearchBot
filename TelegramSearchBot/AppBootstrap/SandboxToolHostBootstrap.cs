using TelegramSearchBot.LLMAgent;

namespace TelegramSearchBot.AppBootstrap {
    public static class SandboxToolHostBootstrap {
        public static void Startup(string[] args) {
            LLMAgentProgram.RunAsync(args).GetAwaiter().GetResult();
        }
    }
}

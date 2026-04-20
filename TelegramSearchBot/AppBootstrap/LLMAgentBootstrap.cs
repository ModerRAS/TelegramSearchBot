using System;
using Serilog;
using TelegramSearchBot.LLMAgent;

namespace TelegramSearchBot.AppBootstrap {
    public class LLMAgentBootstrap : AppBootstrap {
        public static void Startup(string[] args) {
            try {
                LLMAgentProgram.RunAsync(args).GetAwaiter().GetResult();
            } catch (Exception ex) {
                Log.Error(ex, "LLMAgent startup failed.");
                Environment.ExitCode = 1;
            }
        }
    }
}

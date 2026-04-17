using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.AppBootstrap;
using TelegramSearchBot.Common;

namespace TelegramSearchBot.Service.AI.LLM {
    public sealed class LlmAgentProcessLauncher : IAgentProcessLauncher {
        public Task<int> StartAsync(long chatId, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            var dllPath = Path.Combine(AppContext.BaseDirectory, "TelegramSearchBot.LLMAgent.dll");
            if (!File.Exists(dllPath)) {
                throw new FileNotFoundException("LLMAgent executable not found.", dllPath);
            }

            var process = AppBootstrap.AppBootstrap.Fork(
                "dotnet",
                [dllPath, chatId.ToString(), Env.SchedulerPort.ToString()],
                GetMemoryLimitBytes());
            return Task.FromResult(process.Id);
        }

        public bool TryKill(int processId) {
            try {
                using var process = Process.GetProcessById(processId);
                process.Kill(true);
                return true;
            } catch {
                return false;
            }
        }

        private static long? GetMemoryLimitBytes() {
            if (Env.AgentProcessMemoryLimitMb <= 0) {
                return null;
            }

            return (long)Env.AgentProcessMemoryLimitMb * 1024L * 1024L;
        }
    }
}

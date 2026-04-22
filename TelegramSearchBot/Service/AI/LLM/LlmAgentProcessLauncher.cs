using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.AppBootstrap;
using TelegramSearchBot.Common;

namespace TelegramSearchBot.Service.AI.LLM {
    public sealed class LlmAgentProcessLauncher : IAgentProcessLauncher {
        public Task<int> StartAsync(long chatId, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            var process = AppBootstrap.AppBootstrap.Fork(
                ["LLMAgent", chatId.ToString(), Env.SchedulerPort.ToString()],
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

            return ( long ) Env.AgentProcessMemoryLimitMb * 1024L * 1024L;
        }
    }
}

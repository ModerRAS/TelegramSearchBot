using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.LLMAgent.Service {
    public interface IAgentTaskExecutor {
        IAsyncEnumerable<string> CallAsync(
            AgentExecutionTask task,
            LlmExecutionContext executionContext,
            CancellationToken cancellationToken);
    }
}

using TelegramSearchBot.LLMAgent.Service;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    internal sealed class FakeAgentTaskExecutor : IAgentTaskExecutor {
        private readonly Func<AgentExecutionTask, LlmExecutionContext, CancellationToken, IAsyncEnumerable<string>> _handler;

        public FakeAgentTaskExecutor(Func<AgentExecutionTask, LlmExecutionContext, CancellationToken, IAsyncEnumerable<string>> handler) {
            _handler = handler;
        }

        public IAsyncEnumerable<string> CallAsync(AgentExecutionTask task, LlmExecutionContext executionContext, CancellationToken cancellationToken) {
            return _handler(task, executionContext, cancellationToken);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Interface.AI.LLM {
    public interface ILLMService {
        public IAsyncEnumerable<string> ExecAsync(Message message, long ChatId, string modelName, LLMChannel channel,
                                                  [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute LLM with an execution context that can signal iteration limit reached
        /// and carry snapshot data for persistence (without polluting the yield stream).
        /// </summary>
        public IAsyncEnumerable<string> ExecAsync(Message message, long ChatId, string modelName, LLMChannel channel,
                                                  LlmExecutionContext executionContext,
                                                  [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            // Default: delegate to the original ExecAsync (backward compatible)
            return ExecAsync(message, ChatId, modelName, channel, cancellationToken);
        }

        /// <summary>
        /// Resume LLM execution from a previously saved snapshot.
        /// The snapshot contains the full provider history, allowing seamless continuation.
        /// </summary>
        public IAsyncEnumerable<string> ResumeFromSnapshotAsync(LlmContinuationSnapshot snapshot, LLMChannel channel,
                                                                 LlmExecutionContext executionContext,
                                                                 [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            // Default: empty stream (services that don't support it just return nothing)
            return AsyncEnumerable.Empty<string>();
        }

        public Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel);
        public Task<IEnumerable<string>> GetAllModels(LLMChannel channel);

        /// <summary>
        /// 获取指定通道的所有模型及其能力信息
        /// </summary>
        public Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel);

        public Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, string prompt = null);
        public virtual async Task<bool> IsHealthyAsync(LLMChannel channel) => ( await GetAllModels(channel) ).Any();
    }
}

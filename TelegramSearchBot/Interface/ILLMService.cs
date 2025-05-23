using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using System.Threading; // Added for CancellationToken

namespace TelegramSearchBot.Interface {
    public interface ILLMService {
        public IAsyncEnumerable<string> ExecAsync(Message message, long ChatId, string modelName, LLMChannel channel,
                                                  [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default);
        public Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel);
        public Task<IEnumerable<string>> GetAllModels(LLMChannel channel);
        public virtual async Task<bool> IsHealthyAsync(LLMChannel channel) => (await GetAllModels(channel)).Any();
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Interface.AI.LLM
{
    public interface IGeneralLLMService
    {
        string ServiceName { get; }

        IAsyncEnumerable<string> ExecAsync(Message message, long ChatId, string modelName, ILLMService service, LLMChannel channel, CancellationToken cancellation);
        Task<string> AnalyzeImageAsync(string PhotoPath, long ChatId, CancellationToken cancellationToken = default);
        Task<float[]> GenerateEmbeddingsAsync(string message, CancellationToken cancellationToken = default);
    }
}

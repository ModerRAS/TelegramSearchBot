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

        Task<List<LLMChannel>> GetChannelsAsync(string modelName);
        IAsyncEnumerable<string> ExecAsync(Message message, long ChatId, CancellationToken cancellationToken = default);
        IAsyncEnumerable<string> ExecAsync(Message message, long ChatId, string modelName, ILLMService service, LLMChannel channel, CancellationToken cancellation);
        IAsyncEnumerable<TResult> ExecOperationAsync<TResult>(Func<ILLMService, LLMChannel, CancellationToken, IAsyncEnumerable<TResult>> operation, string modelName, CancellationToken cancellationToken = default);
        Task<string> AnalyzeImageAsync(string PhotoPath, long ChatId, CancellationToken cancellationToken = default);
        IAsyncEnumerable<string> AnalyzeImageAsync(string PhotoPath, long ChatId, string modelName, ILLMService service, LLMChannel channel, CancellationToken cancellationToken = default);
        Task<float[]> GenerateEmbeddingsAsync(Message message, long ChatId);
        Task<float[]> GenerateEmbeddingsAsync(string message, CancellationToken cancellationToken = default);
        IAsyncEnumerable<float[]> GenerateEmbeddingsAsync(string message, string modelName, ILLMService service, LLMChannel channel, CancellationToken cancellationToken = default);
        Task<int> GetAltPhotoAvailableCapacityAsync();
        Task<int> GetAvailableCapacityAsync(string modelName = "gemma3:27b");
    }
}

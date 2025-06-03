using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM.Decorators
{
    /// <summary>
    /// LLM服务装饰器基类
    /// </summary>
    public abstract class BaseLLMDecorator : ILLMStreamService
    {
        protected readonly ILLMStreamService _innerService;

        protected BaseLLMDecorator(ILLMStreamService innerService)
        {
            _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
        }

        public virtual async IAsyncEnumerable<string> ExecAsync(
            Message message, 
            long chatId, 
            string modelName, 
            LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var token in _innerService.ExecAsync(message, chatId, modelName, channel, cancellationToken))
            {
                yield return token;
            }
        }

        public virtual Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
        {
            return _innerService.AnalyzeImageAsync(photoPath, modelName, channel);
        }

        public virtual Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
        {
            return _innerService.GenerateEmbeddingsAsync(text, modelName, channel);
        }

        public virtual Task<bool> IsHealthyAsync(LLMChannel channel)
        {
            return _innerService.IsHealthyAsync(channel);
        }

        public virtual Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
        {
            return _innerService.GetAllModels(channel);
        }

        public virtual Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
        {
            return _innerService.GetAllModelsWithCapabilities(channel);
        }
    }
} 
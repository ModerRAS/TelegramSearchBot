using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM.Adapters
{
    /// <summary>
    /// LLM服务适配器 - 将现有的LLM服务适配为ILLMStreamService接口
    /// </summary>
    public class LLMServiceAdapter : ILLMStreamService
    {
        private readonly object _innerService;
        private readonly string _serviceType;

        public LLMServiceAdapter(object innerService)
        {
            _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
            _serviceType = innerService.GetType().Name;
        }

        public async IAsyncEnumerable<string> ExecAsync(
            Message message, 
            long chatId, 
            string modelName, 
            LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 使用反射调用原始服务的ExecAsync方法
            var method = _innerService.GetType().GetMethod("ExecAsync", 
                new[] { typeof(Message), typeof(long), typeof(string), typeof(LLMChannel), typeof(CancellationToken) });
            
            if (method != null)
            {
                var result = method.Invoke(_innerService, new object[] { message, chatId, modelName, channel, cancellationToken });
                if (result is IAsyncEnumerable<string> asyncEnumerable)
                {
                    await foreach (var token in asyncEnumerable)
                    {
                        yield return token;
                    }
                }
            }
            else
            {
                throw new NotSupportedException($"Service {_serviceType} does not support ExecAsync method");
            }
        }

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel)
        {
            var method = _innerService.GetType().GetMethod("AnalyzeImageAsync", 
                new[] { typeof(string), typeof(string), typeof(LLMChannel) });
            
            if (method != null)
            {
                var result = method.Invoke(_innerService, new object[] { photoPath, modelName, channel });
                if (result is Task<string> task)
                {
                    return await task;
                }
            }
            
            throw new NotSupportedException($"Service {_serviceType} does not support AnalyzeImageAsync method");
        }

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
        {
            var method = _innerService.GetType().GetMethod("GenerateEmbeddingsAsync", 
                new[] { typeof(string), typeof(string), typeof(LLMChannel) });
            
            if (method != null)
            {
                var result = method.Invoke(_innerService, new object[] { text, modelName, channel });
                if (result is Task<float[]> task)
                {
                    return await task;
                }
            }
            
            throw new NotSupportedException($"Service {_serviceType} does not support GenerateEmbeddingsAsync method");
        }

        public async Task<bool> IsHealthyAsync(LLMChannel channel)
        {
            var method = _innerService.GetType().GetMethod("IsHealthyAsync", 
                new[] { typeof(LLMChannel) });
            
            if (method != null)
            {
                var result = method.Invoke(_innerService, new object[] { channel });
                if (result is Task<bool> task)
                {
                    return await task;
                }
                if (result is bool boolResult)
                {
                    return boolResult;
                }
            }
            
            // 默认返回true，表示健康
            return true;
        }

        public async Task<IEnumerable<string>> GetAllModels(LLMChannel channel)
        {
            var method = _innerService.GetType().GetMethod("GetAllModels", 
                new[] { typeof(LLMChannel) });
            
            if (method != null)
            {
                var result = method.Invoke(_innerService, new object[] { channel });
                if (result is Task<IEnumerable<string>> task)
                {
                    return await task;
                }
                if (result is IEnumerable<string> enumerable)
                {
                    return enumerable;
                }
            }
            
            return new List<string>();
        }

        public async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel)
        {
            var method = _innerService.GetType().GetMethod("GetAllModelsWithCapabilities", 
                new[] { typeof(LLMChannel) });
            
            if (method != null)
            {
                var result = method.Invoke(_innerService, new object[] { channel });
                if (result is Task<IEnumerable<ModelWithCapabilities>> task)
                {
                    return await task;
                }
                if (result is IEnumerable<ModelWithCapabilities> enumerable)
                {
                    return enumerable;
                }
            }
            
            return new List<ModelWithCapabilities>();
        }
    }
} 
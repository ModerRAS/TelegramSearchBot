using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.LLM.Domain.Factories;
using TelegramSearchBot.Model.Data;
using System.Threading.Channels;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class LLMFactory : IService, ILLMFactory {
        public string ServiceName => "LLMFactory";

        private readonly ILogger<LLMFactory> _logger;
        private readonly ILLMServiceFactoryManager _factoryManager;

        public LLMFactory(
            ILogger<LLMFactory> logger,
            ILLMServiceFactoryManager factoryManager
            ) {
            _logger = logger;
            _factoryManager = factoryManager;
            
            _logger.LogInformation("LLMFactory initialized with factory manager.");
        }

        public ILLMService GetLLMService(LLMProvider provider) {
            try {
                var service = _factoryManager.GetService((TelegramSearchBot.LLM.Domain.Entities.LLMProvider)provider);
                _logger.LogInformation("Successfully created LLM service for provider: {Provider}", provider);
                return new LLMServiceAdapter(service);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to create LLM service for provider: {Provider}", provider);
                throw;
            }
        }
    }
    
    /// <summary>
    /// 适配器类，用于桥接新旧LLM接口
    /// </summary>
    internal class LLMServiceAdapter : ILLMService
    {
        private readonly TelegramSearchBot.LLM.Domain.Services.ILLMService _modernService;
        
        public LLMServiceAdapter(TelegramSearchBot.LLM.Domain.Services.ILLMService modernService)
        {
            _modernService = modernService ?? throw new ArgumentNullException(nameof(modernService));
        }

        // Helper to convert LLMChannelDto to LLMChannelConfig
        private TelegramSearchBot.LLM.Domain.ValueObjects.LLMChannelConfig ConvertOldChannelToNewConfig(LLMChannelDto oldChannel)
        {
            if (oldChannel == null) return null;
            
            // Attempt to get OrganizationId and ProxyUrl from ExtendedConfig if they exist
            string organizationId = null;
            if (oldChannel.ExtendedConfig != null && oldChannel.ExtendedConfig.TryGetValue("OrganizationId", out var orgIdObj) && orgIdObj is string orgIdStr)
            {
                organizationId = orgIdStr;
            }

            string proxyUrl = null;
            if (oldChannel.ExtendedConfig != null && oldChannel.ExtendedConfig.TryGetValue("ProxyUrl", out var proxyUrlObj) && proxyUrlObj is string proxyUrlStr)
            {
                proxyUrl = proxyUrlStr;
            }

            return new TelegramSearchBot.LLM.Domain.ValueObjects.LLMChannelConfig(
                Gateway: oldChannel.Gateway,
                ApiKey: oldChannel.ApiKey,
                OrganizationId: organizationId,
                ProxyUrl: proxyUrl,
                TimeoutSeconds: oldChannel.TimeoutSeconds
            );
        }
        
        // Helper to convert TelegramSearchBot.Model.Data.LLMChannel to LLMChannelConfig
        private TelegramSearchBot.LLM.Domain.ValueObjects.LLMChannelConfig ConvertDataChannelToNewConfig(TelegramSearchBot.Model.Data.LLMChannel dataChannel)
        {
            if (dataChannel == null) return null;
            var dto = LLMChannelDto.FromDataModel(dataChannel);
            return ConvertOldChannelToNewConfig(dto);
        }

        // Helper to convert old LLMRole to new LLMRole
        private TelegramSearchBot.LLM.Domain.ValueObjects.LLMRole ConvertOldRoleToNew(TelegramSearchBot.Model.AI.LLMRole oldRole)
        {
            return oldRole switch
            {
                TelegramSearchBot.Model.AI.LLMRole.System => TelegramSearchBot.LLM.Domain.ValueObjects.LLMRole.System,
                TelegramSearchBot.Model.AI.LLMRole.User => TelegramSearchBot.LLM.Domain.ValueObjects.LLMRole.User,
                TelegramSearchBot.Model.AI.LLMRole.Assistant => TelegramSearchBot.LLM.Domain.ValueObjects.LLMRole.Assistant,
                TelegramSearchBot.Model.AI.LLMRole.Tool => TelegramSearchBot.LLM.Domain.ValueObjects.LLMRole.Tool,
                _ => throw new ArgumentOutOfRangeException(nameof(oldRole), $"Unsupported role: {oldRole}")
            };
        }

        // Helper to convert old LLMContentType to new LLMContentType
        private TelegramSearchBot.LLM.Domain.ValueObjects.LLMContentType ConvertOldContentTypeToNew(TelegramSearchBot.Model.AI.LLMContentType oldContentType)
        {
            return oldContentType switch
            {
                TelegramSearchBot.Model.AI.LLMContentType.Text => TelegramSearchBot.LLM.Domain.ValueObjects.LLMContentType.Text,
                TelegramSearchBot.Model.AI.LLMContentType.Image => TelegramSearchBot.LLM.Domain.ValueObjects.LLMContentType.Image,
                // Assuming Audio and File map directly. Others might need specific handling or be ignored.
                TelegramSearchBot.Model.AI.LLMContentType.Audio => TelegramSearchBot.LLM.Domain.ValueObjects.LLMContentType.Audio,
                TelegramSearchBot.Model.AI.LLMContentType.File => TelegramSearchBot.LLM.Domain.ValueObjects.LLMContentType.File,
                _ => TelegramSearchBot.LLM.Domain.ValueObjects.LLMContentType.Text // Default or throw
            };
        }
        
        // Helper to convert old LLMImageContent to new LLMImageContent
        private TelegramSearchBot.LLM.Domain.ValueObjects.LLMImageContent ConvertOldImageContentToNew(TelegramSearchBot.Model.AI.LLMImageContent oldImage)
        {
            if (oldImage == null) return null;
            return new TelegramSearchBot.LLM.Domain.ValueObjects.LLMImageContent(
                Data: oldImage.Data,
                Url: oldImage.Url,
                MimeType: oldImage.MimeType
            );
        }

        // Helper to convert old LLMContent list to new LLMContent list
        private List<TelegramSearchBot.LLM.Domain.ValueObjects.LLMContent> ConvertOldContentsToNew(List<TelegramSearchBot.Model.AI.LLMContent> oldContents)
        {
            if (oldContents == null) return new List<TelegramSearchBot.LLM.Domain.ValueObjects.LLMContent>();
            return oldContents.Select(oc => new TelegramSearchBot.LLM.Domain.ValueObjects.LLMContent(
                Type: ConvertOldContentTypeToNew(oc.Type),
                Text: oc.Text,
                Image: ConvertOldImageContentToNew(oc.Image)
                // Audio and File content are not in the new simple LLMContent record, handle as needed or ignore
            )).ToList();
        }

        // Helper to convert old LLMMessage list to new LLMMessage list
        private List<TelegramSearchBot.LLM.Domain.ValueObjects.LLMMessage> ConvertOldMessagesToNew(List<TelegramSearchBot.Model.AI.LLMMessage> oldMessages, TelegramSearchBot.Model.AI.LLMMessage currentMessage)
        {
            var allMessages = new List<TelegramSearchBot.Model.AI.LLMMessage>(oldMessages ?? new List<TelegramSearchBot.Model.AI.LLMMessage>());
            if (currentMessage != null)
            {
                allMessages.Add(currentMessage); // Add current message to the history for the new request format
            }

            return allMessages.Select(om => new TelegramSearchBot.LLM.Domain.ValueObjects.LLMMessage(
                Role: ConvertOldRoleToNew(om.Role),
                Content: om.Content, // Primary content
                Contents: ConvertOldContentsToNew(om.Contents) // Multi-modal parts
            )).ToList();
        }

        // Helper to convert old LLMRequest to new LLMRequest
        private TelegramSearchBot.LLM.Domain.ValueObjects.LLMRequest ConvertOldRequestToNew(TelegramSearchBot.Model.AI.LLMRequest oldRequest)
        {
            return new TelegramSearchBot.LLM.Domain.ValueObjects.LLMRequest(
                RequestId: oldRequest.RequestId,
                Model: oldRequest.Model,
                Channel: ConvertOldChannelToNewConfig(oldRequest.Channel),
                ChatHistory: ConvertOldMessagesToNew(oldRequest.ChatHistory, oldRequest.CurrentMessage),
                SystemPrompt: oldRequest.SystemPrompt
            );
        }

        // Helper to convert new LLMResponse to old LLMResponse
        private TelegramSearchBot.Model.AI.LLMResponse ConvertNewResponseToOld(TelegramSearchBot.LLM.Domain.ValueObjects.LLMResponse newResponse, DateTime? requestStartTimeFromOldReq)
        {
            return new TelegramSearchBot.Model.AI.LLMResponse
            {
                RequestId = newResponse.RequestId,
                Model = newResponse.Model,
                IsSuccess = newResponse.IsSuccess,
                Content = newResponse.Content,
                ErrorMessage = newResponse.ErrorMessage,
                StartTime = requestStartTimeFromOldReq ?? newResponse.StartTime ?? DateTime.UtcNow, // Prioritize original start time
                EndTime = newResponse.EndTime ?? DateTime.UtcNow,
                // TokenUsage, ToolCalls, etc., are not directly mapped from the new simple response.
                // Initialize them to default or empty if necessary.
                TokenUsage = new TelegramSearchBot.Model.AI.LLMTokenUsage(), 
                ToolCalls = new List<TelegramSearchBot.Model.AI.LLMToolCall>()
            };
        }

        public async Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var newRequest = ConvertOldRequestToNew(request);
            var modernResponse = await _modernService.ExecuteAsync(newRequest, cancellationToken);
            // Preserve the original StartTime from the old request if possible, as new LLMRequest doesn't carry it.
            // The new LLMResponse might have its own StartTime, which is likely closer to the actual service call.
            // For now, let's assume the newResponse.StartTime is more accurate for the _modernService call.
            return ConvertNewResponseToOld(modernResponse, request.Context?.Properties.TryGetValue("OriginalStartTime", out var st) == true && st is DateTime originalStartTime ? originalStartTime : newRequest.StartTime );
        }

        public async Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var newRequest = ConvertOldRequestToNew(request);
            var (streamReader, modernResponseTask) = await _modernService.ExecuteStreamAsync(newRequest, cancellationToken);
            
            var adaptedResponseTask = modernResponseTask.ContinueWith(task => 
                ConvertNewResponseToOld(task.Result, request.Context?.Properties.TryGetValue("OriginalStartTime", out var st) == true && st is DateTime originalStartTime ? originalStartTime : newRequest.StartTime), 
                cancellationToken);

            return (streamReader, adaptedResponseTask);
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text, string model, LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            var newChannelConfig = ConvertOldChannelToNewConfig(channel);
            return await _modernService.GenerateEmbeddingAsync(text, model, newChannelConfig, cancellationToken);
        }

        public async Task<List<string>> GetAvailableModelsAsync(LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            var newChannelConfig = ConvertOldChannelToNewConfig(channel);
            return await _modernService.GetAvailableModelsAsync(newChannelConfig, cancellationToken);
        }

        public async Task<bool> IsHealthyAsync(LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            var newChannelConfig = ConvertOldChannelToNewConfig(channel);
            return await _modernService.IsHealthyAsync(newChannelConfig, cancellationToken);
        }

        public async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(TelegramSearchBot.Model.Data.LLMChannel channel)
        {
            // This method requires TelegramSearchBot.Model.Data.LLMChannel as input per ILLMService interface.
            // The _modernService.GetAvailableModelsAsync takes LLMChannelConfig.
            // We need to convert: LLMChannel (Data) -> LLMChannelDto -> LLMChannelConfig (Domain)
            
            CancellationToken cancellationToken = default; // Assuming a default token if not passed.

            var channelDto = LLMChannelDto.FromDataModel(channel);
            if (channelDto == null)
            {
                // Or throw, or return empty, based on desired error handling.
                return Enumerable.Empty<ModelWithCapabilities>();
            }
            var newChannelConfig = ConvertOldChannelToNewConfig(channelDto);

            var modelNames = await _modernService.GetAvailableModelsAsync(newChannelConfig, cancellationToken);
            
            var result = new List<ModelWithCapabilities>();
            foreach (var modelName in modelNames)
            {
                var capabilities = new ModelWithCapabilities { ModelName = modelName };
                // Populate basic capabilities based on name or leave empty if not determinable
                if (modelName.ToLower().Contains("embed"))
                {
                    capabilities.SetCapability("embedding", true);
                }
                // Add other simple heuristics if possible, or leave Capabilities dictionary empty.
                // The new ILLMService doesn't provide detailed capabilities per model.
                result.Add(capabilities);
            }
            return result;
        }

        // 添加缺失的ILLMService接口方法实现
        public async IAsyncEnumerable<string> ExecAsync(
            TelegramSearchBot.Model.Data.Message message, 
            long chatId, 
            string modelName, 
            TelegramSearchBot.Model.Data.LLMChannel channel, 
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 构建旧版本的LLMRequest来适配新服务
            var channelDto = LLMChannelDto.FromDataModel(channel);
            var llmMessage = new TelegramSearchBot.Model.AI.LLMMessage
            {
                Role = TelegramSearchBot.Model.AI.LLMRole.User,
                Content = message.Content ?? string.Empty
            };

            var oldRequest = new TelegramSearchBot.Model.AI.LLMRequest
            {
                RequestId = Guid.NewGuid().ToString(),
                Model = modelName,
                Channel = channelDto,
                CurrentMessage = llmMessage,
                ChatHistory = new List<TelegramSearchBot.Model.AI.LLMMessage>()
            };

            // 使用流式执行
            var (streamReader, responseTask) = await ExecuteStreamAsync(oldRequest, cancellationToken);
            
            // 流式返回结果
            await foreach (var chunk in streamReader.ReadAllAsync(cancellationToken))
            {
                yield return chunk;
            }
        }

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, TelegramSearchBot.Model.Data.LLMChannel channel)
        {
            var channelDto = LLMChannelDto.FromDataModel(channel);
            var newChannelConfig = ConvertOldChannelToNewConfig(channelDto);
            
            // 这里需要调用支持图片分析的新服务方法
            // 如果新服务没有图片分析方法，我们可以构建一个包含图片的LLMRequest
            var imageContent = new TelegramSearchBot.Model.AI.LLMContent
            {
                Type = TelegramSearchBot.Model.AI.LLMContentType.Image,
                Image = new TelegramSearchBot.Model.AI.LLMImageContent
                {
                    Data = photoPath, // 假设这是图片路径或base64数据
                    MimeType = "image/jpeg"
                }
            };

            var textContent = new TelegramSearchBot.Model.AI.LLMContent
            {
                Type = TelegramSearchBot.Model.AI.LLMContentType.Text,
                Text = "请分析这张图片的内容"
            };

            var llmMessage = new TelegramSearchBot.Model.AI.LLMMessage
            {
                Role = TelegramSearchBot.Model.AI.LLMRole.User,
                Content = "请分析图片",
                Contents = new List<TelegramSearchBot.Model.AI.LLMContent> { textContent, imageContent }
            };

            var oldRequest = new TelegramSearchBot.Model.AI.LLMRequest
            {
                RequestId = Guid.NewGuid().ToString(),
                Model = modelName,
                Channel = channelDto,
                CurrentMessage = llmMessage,
                ChatHistory = new List<TelegramSearchBot.Model.AI.LLMMessage>()
            };

            var response = await ExecuteAsync(oldRequest);
            return response.Content ?? "图片分析失败";
        }

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, TelegramSearchBot.Model.Data.LLMChannel channel)
        {
            var channelDto = LLMChannelDto.FromDataModel(channel);
            return await GenerateEmbeddingAsync(text, modelName, channelDto);
        }

        public async Task<bool> IsHealthyAsync(TelegramSearchBot.Model.Data.LLMChannel channel)
        {
            var channelDto = LLMChannelDto.FromDataModel(channel);
            return await IsHealthyAsync(channelDto);
        }

        public async Task<IEnumerable<string>> GetAllModels(TelegramSearchBot.Model.Data.LLMChannel channel)
        {
            var channelDto = LLMChannelDto.FromDataModel(channel);
            var models = await GetAvailableModelsAsync(channelDto);
            return models;
        }
    }
}

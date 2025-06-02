using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using OllamaSharp;
using OllamaSharp.Models;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using GenerativeAI;
using GenerativeAI.Types;
using Newtonsoft.Json;
using SkiaSharp;
using OpenAI.Embeddings;

namespace TelegramSearchBot.Service.AI.LLM
{
    /// <summary>
    /// LLM核心适配器服务 - 统一不同LLM提供商的API调用，支持多模态内容
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class LLMCoreService : IService
    {
        public string ServiceName => "LLMCoreService";
        
        private readonly ILogger<LLMCoreService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public LLMCoreService(ILogger<LLMCoreService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// 执行LLM对话 - 统一处理文本和多模态内容
        /// </summary>
        /// <param name="request">LLM请求，可包含多模态内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>最终的响应结果</returns>
        public async Task<LLMResponse> ExecuteAsync(
            LLMRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = new LLMResponse
            {
                RequestId = request.RequestId,
                Model = request.Model,
                StartTime = DateTime.UtcNow
            };

            try
            {
                // 验证请求
                ValidateRequest(request);

                // 根据提供商执行不同的处理逻辑
                switch (request.Provider)
                {
                    case LLMProvider.OpenAI:
                        return await ExecuteOpenAIAsync(request, cancellationToken);
                    case LLMProvider.Ollama:
                        return await ExecuteOllamaAsync(request, cancellationToken);
                    case LLMProvider.Gemini:
                        return await ExecuteGeminiAsync(request, cancellationToken);
                    default:
                        throw new NotSupportedException($"不支持的LLM提供商: {request.Provider}");
                }
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.ErrorMessage = ex.Message;
                response.EndTime = DateTime.UtcNow;
                _logger.LogError(ex, "LLM执行失败: {Error}", ex.Message);
                return response;
            }
        }

        /// <summary>
        /// 执行LLM对话 - 使用Channel进行流式返回，支持多模态内容
        /// </summary>
        /// <param name="request">LLM请求，可包含多模态内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>流式响应的Channel和最终响应</returns>
        public async Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(
            LLMRequest request,
            CancellationToken cancellationToken = default)
        {
            var channel = Channel.CreateUnbounded<string>();
            var responseBuilder = new StringBuilder();

            var responseTask = Task.Run(async () =>
            {
                var response = new LLMResponse
                {
                    RequestId = request.RequestId,
                    Model = request.Model,
                    StartTime = DateTime.UtcNow,
                    IsStreaming = true
                };

                try
                {
                    ValidateRequest(request);

                    // 根据提供商执行流式处理
                    switch (request.Provider)
                    {
                        case LLMProvider.OpenAI:
                            await ExecuteOpenAIStreamAsync(request, channel.Writer, responseBuilder, cancellationToken);
                            break;
                        case LLMProvider.Ollama:
                            await ExecuteOllamaStreamAsync(request, channel.Writer, responseBuilder, cancellationToken);
                            break;
                        case LLMProvider.Gemini:
                            await ExecuteGeminiStreamAsync(request, channel.Writer, responseBuilder, cancellationToken);
                            break;
                        default:
                            throw new NotSupportedException($"不支持的LLM提供商: {request.Provider}");
                    }

                    response.IsSuccess = true;
                    response.Content = responseBuilder.ToString();
                }
                catch (Exception ex)
                {
                    response.IsSuccess = false;
                    response.ErrorMessage = ex.Message;
                    _logger.LogError(ex, "流式LLM执行失败: {Error}", ex.Message);

                    // 发送错误信息到Channel
                    await channel.Writer.WriteAsync($"错误: {ex.Message}", cancellationToken);
                }
                finally
                {
                    response.EndTime = DateTime.UtcNow;
                    channel.Writer.Complete();
                }

                return response;
            }, cancellationToken);

            return (channel.Reader, responseTask);
        }

        /// <summary>
        /// 生成嵌入向量
        /// </summary>
        public async Task<float[]> GenerateEmbeddingAsync(
            string text,
            LLMProvider provider,
            string model,
            LLMChannelDto channel,
            CancellationToken cancellationToken = default)
        {
            try
            {
                switch (provider)
                {
                    case LLMProvider.OpenAI:
                        return await GenerateOpenAIEmbeddingAsync(text, model, channel, cancellationToken);
                    case LLMProvider.Ollama:
                        return await GenerateOllamaEmbeddingAsync(text, model, channel, cancellationToken);
                    case LLMProvider.Gemini:
                        return await GenerateGeminiEmbeddingAsync(text, model, channel, cancellationToken);
                    default:
                        throw new NotSupportedException($"不支持的嵌入提供商: {provider}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成嵌入向量失败: {Error}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 检查LLM服务健康状态
        /// </summary>
        public async Task<bool> IsHealthyAsync(LLMProvider provider, LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            try
            {
                switch (provider)
                {
                    case LLMProvider.OpenAI:
                        return await CheckOpenAIHealthAsync(channel, cancellationToken);
                    case LLMProvider.Ollama:
                        return await CheckOllamaHealthAsync(channel, cancellationToken);
                    case LLMProvider.Gemini:
                        return await CheckGeminiHealthAsync(channel, cancellationToken);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "健康检查失败: {Provider}", provider);
                return false;
            }
        }

        /// <summary>
        /// 获取可用模型列表
        /// </summary>
        public async Task<List<string>> GetAvailableModelsAsync(LLMProvider provider, LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            try
            {
                switch (provider)
                {
                    case LLMProvider.OpenAI:
                        return await GetOpenAIModelsAsync(channel, cancellationToken);
                    case LLMProvider.Ollama:
                        return await GetOllamaModelsAsync(channel, cancellationToken);
                    case LLMProvider.Gemini:
                        return await GetGeminiModelsAsync(channel, cancellationToken);
                    default:
                        return new List<string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取模型列表失败: {Provider}", provider);
                return new List<string>();
            }
        }

        // ==================== OpenAI 适配器实现 ====================
        private async Task<LLMResponse> ExecuteOpenAIAsync(
            LLMRequest request,
            CancellationToken cancellationToken)
        {
            var handler = new HttpClientHandler
            {
                Proxy = WebRequest.DefaultWebProxy,
                UseProxy = true
            };

            using var httpClient = new HttpClient(handler);

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(request.Channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            var chatClient = new ChatClient(model: request.Model, credential: new(request.Channel.ApiKey), clientOptions);

            var chatMessages = ConvertToOpenAIChatMessages(request.ChatHistory, request.SystemPrompt);
            
            var responseBuilder = new StringBuilder();
            await foreach (var update in chatClient.CompleteChatStreamingAsync(chatMessages, cancellationToken: cancellationToken))
            {
                foreach (var updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>())
                {
                    if (updatePart?.Text != null)
                    {
                        responseBuilder.Append(updatePart.Text);
                    }
                }
            }

            return new LLMResponse
            {
                RequestId = request.RequestId,
                Model = request.Model,
                IsSuccess = true,
                Content = responseBuilder.ToString(),
                EndTime = DateTime.UtcNow
            };
        }

        private async Task ExecuteOpenAIStreamAsync(
            LLMRequest request,
            ChannelWriter<string> writer,
            StringBuilder responseBuilder,
            CancellationToken cancellationToken)
        {
            var handler = new HttpClientHandler
            {
                Proxy = WebRequest.DefaultWebProxy,
                UseProxy = true
            };

            using var httpClient = new HttpClient(handler);

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(request.Channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            var chatClient = new ChatClient(model: request.Model, credential: new(request.Channel.ApiKey), clientOptions);

            var chatMessages = ConvertToOpenAIChatMessages(request.ChatHistory, request.SystemPrompt);

            await foreach (var update in chatClient.CompleteChatStreamingAsync(chatMessages, cancellationToken: cancellationToken))
            {
                foreach (var updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>())
                {
                    if (updatePart?.Text != null)
                    {
                        responseBuilder.Append(updatePart.Text);
                        await writer.WriteAsync(updatePart.Text, cancellationToken);
                    }
                }
            }
        }

        private async Task<LLMResponse> ExecuteOllamaAsync(
            LLMRequest request,
            CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory?.CreateClient("OllamaClient") ?? new HttpClient();
            httpClient.BaseAddress = new Uri(request.Channel.Gateway);
            var ollama = new OllamaApiClient(httpClient, request.Model);

            if (!await CheckAndPullOllamaModelAsync(ollama, request.Model))
            {
                throw new Exception($"无法检查或拉取Ollama模型: {request.Model}");
            }

            ollama.SelectedModel = request.Model;
            var systemPrompt = request.SystemPrompt ?? "";
            var chat = new OllamaSharp.Chat(ollama, systemPrompt);

            var responseBuilder = new StringBuilder();
            
            // 处理多模态内容
            var (textContent, images) = ExtractMultimodalContent(request.ChatHistory);

            if (images.Any())
            {
                // 处理包含图像的消息
                await foreach (var token in chat.SendAsync(textContent, images.ToArray(), cancellationToken))
                {
                    responseBuilder.Append(token);
                }
            }
            else
            {
                // 纯文本消息
                await foreach (var token in chat.SendAsync(textContent, cancellationToken))
                {
                    responseBuilder.Append(token);
                }
            }

            return new LLMResponse
            {
                RequestId = request.RequestId,
                Model = request.Model,
                IsSuccess = true,
                Content = responseBuilder.ToString(),
                EndTime = DateTime.UtcNow
            };
        }

        private async Task ExecuteOllamaStreamAsync(
            LLMRequest request,
            ChannelWriter<string> writer,
            StringBuilder responseBuilder,
            CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory?.CreateClient("OllamaClient") ?? new HttpClient();
            httpClient.BaseAddress = new Uri(request.Channel.Gateway);
            var ollama = new OllamaApiClient(httpClient, request.Model);

            if (!await CheckAndPullOllamaModelAsync(ollama, request.Model))
            {
                throw new Exception($"无法检查或拉取Ollama模型: {request.Model}");
            }

            ollama.SelectedModel = request.Model;
            var systemPrompt = request.SystemPrompt ?? "";
            var chat = new OllamaSharp.Chat(ollama, systemPrompt);

            // 处理多模态内容
            var (textContent, images) = ExtractMultimodalContent(request.ChatHistory);

            if (images.Any())
            {
                // 处理包含图像的消息
                await foreach (var token in chat.SendAsync(textContent, images.ToArray()))
                {
                    responseBuilder.Append(token);
                    await writer.WriteAsync(token, cancellationToken);
                }
            }
            else
            {
                // 纯文本消息
                await foreach (var token in chat.SendAsync(textContent, cancellationToken))
                {
                    responseBuilder.Append(token);
                    await writer.WriteAsync(token, cancellationToken);
                }
            }
        }

        private async Task<LLMResponse> ExecuteGeminiAsync(
            LLMRequest request,
            CancellationToken cancellationToken)
        {
            var googleAI = new GoogleAi(request.Channel.ApiKey, client: _httpClientFactory.CreateClient());
            var model = googleAI.CreateGenerativeModel("models/" + request.Model);

            var responseBuilder = new StringBuilder();
            
            // 检查是否包含多模态内容
            if (HasMultimodalContent(request.ChatHistory))
            {
                // 处理多模态对话
                var contentRequest = await ConvertToGeminiMultimodalRequest(request.ChatHistory, request.SystemPrompt);
                var chat = model.StartChat();
                var response = await chat.GenerateContentAsync(contentRequest);
                responseBuilder.Append(response.Text);
            }
            else
            {
                // 纯文本对话
                var history = ConvertToGeminiHistory(request.ChatHistory, request.SystemPrompt);
                var chatSession = model.StartChat(history: history);
                var lastMessage = request.ChatHistory.LastOrDefault()?.Content ?? "";

                await foreach (var chunk in chatSession.StreamContentAsync(lastMessage))
                {
                    responseBuilder.Append(chunk.Text);
                }
            }

            return new LLMResponse
            {
                RequestId = request.RequestId,
                Model = request.Model,
                IsSuccess = true,
                Content = responseBuilder.ToString(),
                EndTime = DateTime.UtcNow
            };
        }

        private async Task ExecuteGeminiStreamAsync(
            LLMRequest request,
            ChannelWriter<string> writer,
            StringBuilder responseBuilder,
            CancellationToken cancellationToken)
        {
            var googleAI = new GoogleAi(request.Channel.ApiKey, client: _httpClientFactory.CreateClient());
            var model = googleAI.CreateGenerativeModel("models/" + request.Model);

            // 检查是否包含多模态内容
            if (HasMultimodalContent(request.ChatHistory))
            {
                // 多模态内容通常不支持流式，返回完整响应
                var contentRequest = await ConvertToGeminiMultimodalRequest(request.ChatHistory, request.SystemPrompt);
                var chat = model.StartChat();
                var response = await chat.GenerateContentAsync(contentRequest);
                var content = response.Text;
                responseBuilder.Append(content);
                await writer.WriteAsync(content, cancellationToken);
            }
            else
            {
                // 纯文本流式对话
                var history = ConvertToGeminiHistory(request.ChatHistory, request.SystemPrompt);
                var chatSession = model.StartChat(history: history);
                var lastMessage = request.ChatHistory.LastOrDefault()?.Content ?? "";

                await foreach (var chunk in chatSession.StreamContentAsync(lastMessage))
                {
                    responseBuilder.Append(chunk.Text);
                    await writer.WriteAsync(chunk.Text, cancellationToken);
                }
            }
        }

        // ==================== 嵌入向量生成适配器 ====================
        private async Task<float[]> GenerateOpenAIEmbeddingAsync(
            string text, string model, LLMChannelDto channel, CancellationToken cancellationToken)
        {
            var handler = new HttpClientHandler
            {
                Proxy = WebRequest.DefaultWebProxy,
                UseProxy = true
            };

            using var httpClient = new HttpClient(handler);

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            var client = new OpenAIClient(new ApiKeyCredential(channel.ApiKey), clientOptions);
            var embeddingClient = client.GetEmbeddingClient(model);
            
            var response = await embeddingClient.GenerateEmbeddingsAsync(new[] { text });
            
            if (response?.Value != null && response.Value.Any())
            {
                var embedding = response.Value.First();
                var embeddingProp = embedding.GetType().GetProperty("Embedding") 
                                  ?? embedding.GetType().GetProperty("EmbeddingVector")
                                  ?? embedding.GetType().GetProperty("Vector");
                
                if (embeddingProp != null)
                {
                    var embeddingValue = embeddingProp.GetValue(embedding);
                    if (embeddingValue is float[] floatArray)
                    {
                        return floatArray;
                    }
                    else if (embeddingValue is IEnumerable<float> floatEnumerable)
                    {
                        return floatEnumerable.ToArray();
                    }
                }
            }
            
            return new float[1536]; // OpenAI默认维度
        }

        private async Task<float[]> GenerateOllamaEmbeddingAsync(
            string text, string model, LLMChannelDto channel, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
            httpClient.BaseAddress = new Uri(channel.Gateway);
            var ollama = new OllamaApiClient(httpClient, model);

            if (!await CheckAndPullOllamaModelAsync(ollama, model))
            {
                throw new Exception($"无法检查或拉取Ollama模型: {model}");
            }

            var embedRequest = new EmbedRequest
            {
                Model = model,
                Input = new List<string> { text }
            };
            
            var embeddings = await ollama.EmbedAsync(embedRequest, cancellationToken);
            return embeddings.Embeddings.FirstOrDefault() ?? Array.Empty<float>();
        }

        private async Task<float[]> GenerateGeminiEmbeddingAsync(
            string text, string model, LLMChannelDto channel, CancellationToken cancellationToken)
        {
            var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
            var embeddings = googleAI.CreateEmbeddingModel("models/embedding-001");
            var response = await embeddings.EmbedContentAsync(text);
            return response.Embedding.Values.Select(v => (float)v).ToArray();
        }

        // ==================== 健康检查适配器 ====================
        private async Task<bool> CheckOpenAIHealthAsync(LLMChannelDto channel, CancellationToken cancellationToken)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    Proxy = WebRequest.DefaultWebProxy,
                    UseProxy = true
                };

                using var httpClient = new HttpClient(handler);

                var clientOptions = new OpenAIClientOptions
                {
                    Endpoint = new Uri(channel.Gateway),
                    Transport = new HttpClientPipelineTransport(httpClient),
                };

                var client = new OpenAIClient(new ApiKeyCredential(channel.ApiKey), clientOptions);
                var modelClient = client.GetOpenAIModelClient();
                var models = await modelClient.GetModelsAsync(cancellationToken);
                return models?.Value != null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenAI健康检查失败");
                return false;
            }
        }

        private async Task<bool> CheckOllamaHealthAsync(LLMChannelDto channel, CancellationToken cancellationToken)
        {
            try
            {
                var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
                httpClient.BaseAddress = new Uri(channel.Gateway);
                var ollama = new OllamaApiClient(httpClient);
                
                var models = await ollama.ListLocalModelsAsync();
                return models != null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama健康检查失败");
                return false;
            }
        }

        private async Task<bool> CheckGeminiHealthAsync(LLMChannelDto channel, CancellationToken cancellationToken)
        {
            try
            {
                var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
                var modelsResponse = await googleAI.ListModelsAsync();
                return modelsResponse?.Models != null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini健康检查失败");
                return false;
            }
        }

        // ==================== 模型列表获取适配器 ====================
        private async Task<List<string>> GetOpenAIModelsAsync(LLMChannelDto channel, CancellationToken cancellationToken)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    Proxy = WebRequest.DefaultWebProxy,
                    UseProxy = true
                };

                using var httpClient = new HttpClient(handler);

                var clientOptions = new OpenAIClientOptions
                {
                    Endpoint = new Uri(channel.Gateway),
                    Transport = new HttpClientPipelineTransport(httpClient),
                };

                var client = new OpenAIClient(new ApiKeyCredential(channel.ApiKey), clientOptions);
                var modelClient = client.GetOpenAIModelClient();
                var models = await modelClient.GetModelsAsync(cancellationToken);
                
                return models.Value.Select(m => m.Id).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取OpenAI模型列表失败");
                return new List<string>();
            }
        }

        private async Task<List<string>> GetOllamaModelsAsync(LLMChannelDto channel, CancellationToken cancellationToken)
        {
            try
            {
                var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient();
                httpClient.BaseAddress = new Uri(channel.Gateway);
                var ollama = new OllamaApiClient(httpClient);
                
                var models = await ollama.ListLocalModelsAsync();
                return models.Select(m => m.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取Ollama模型列表失败");
                return new List<string>();
            }
        }

        private async Task<List<string>> GetGeminiModelsAsync(LLMChannelDto channel, CancellationToken cancellationToken)
        {
            try
            {
                var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
                var modelsResponse = await googleAI.ListModelsAsync();
                return modelsResponse.Models.Select(m => m.Name.Replace("models/", "")).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取Gemini模型列表失败");
                return new List<string>();
            }
        }

        // ==================== 多模态内容处理辅助方法 ====================
        
        /// <summary>
        /// 检查聊天历史是否包含多模态内容
        /// </summary>
        private bool HasMultimodalContent(List<LLMMessage> messages)
        {
            return messages.Any(m => m.Contents.Any(c => 
                c.Type == LLMContentType.Image || 
                c.Type == LLMContentType.Audio || 
                c.Type == LLMContentType.File));
        }

        /// <summary>
        /// 从聊天历史中提取文本内容和图像（用于Ollama）
        /// </summary>
        private (string textContent, List<string> images) ExtractMultimodalContent(List<LLMMessage> messages)
        {
            var textBuilder = new StringBuilder();
            var images = new List<string>();

            foreach (var message in messages)
            {
                // 添加文本内容
                if (!string.IsNullOrEmpty(message.Content))
                {
                    textBuilder.AppendLine(message.Content);
                }

                // 提取图像
                foreach (var content in message.Contents.Where(c => c.Type == LLMContentType.Image))
                {
                    if (content.Image != null && !string.IsNullOrEmpty(content.Image.Data))
                    {
                        images.Add(content.Image.Data);
                    }
                }
            }

            return (textBuilder.ToString().Trim(), images);
        }

        /// <summary>
        /// 转换为Gemini多模态请求（处理图像内容）
        /// </summary>
        private async Task<GenerateContentRequest> ConvertToGeminiMultimodalRequest(List<LLMMessage> messages, string systemPrompt)
        {
            var request = new GenerateContentRequest();
            
            // 添加系统提示
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                request.AddText(systemPrompt);
            }

            // 处理最后一条消息（通常包含多模态内容）
            var lastMessage = messages.LastOrDefault();
            if (lastMessage != null)
            {
                // 添加文本内容
                if (!string.IsNullOrEmpty(lastMessage.Content))
                {
                    request.AddText(lastMessage.Content);
                }

                // 处理图像内容
                foreach (var content in lastMessage.Contents.Where(c => c.Type == LLMContentType.Image))
                {
                    if (content.Image != null)
                    {
                        if (!string.IsNullOrEmpty(content.Image.Data))
                        {
                            // Base64数据，保存到临时文件
                            var imageBytes = Convert.FromBase64String(content.Image.Data);
                            var tempPath = Path.GetTempFileName();
                            await File.WriteAllBytesAsync(tempPath, imageBytes);
                            request.AddInlineFile(tempPath);
                        }
                        else if (!string.IsNullOrEmpty(content.Image.Url))
                        {
                            // URL图像，下载到临时文件
                            using var client = new HttpClient();
                            var imageBytes = await client.GetByteArrayAsync(content.Image.Url);
                            var tempPath = Path.GetTempFileName();
                            await File.WriteAllBytesAsync(tempPath, imageBytes);
                            request.AddInlineFile(tempPath);
                        }
                    }
                }
            }

            return request;
        }

        // ==================== 辅助方法 ====================
        private async Task<bool> CheckAndPullOllamaModelAsync(OllamaApiClient ollama, string modelName)
        {
            try
            {
                var models = await ollama.ListLocalModelsAsync();
                if (models.Any(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || 
                                   m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                _logger.LogInformation("模型 {ModelName} 不存在本地，开始拉取...", modelName);
                await foreach (var status in ollama.PullModelAsync(modelName, CancellationToken.None))
                {
                    _logger.LogInformation("拉取模型 {ModelName}: {Percent}% - {Status}", 
                        modelName, status.Percent, status.Status);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查或拉取Ollama模型失败: {ModelName}", modelName);
                return false;
            }
        }

        private List<ChatMessage> ConvertToOpenAIChatMessages(List<LLMMessage> messages, string systemPrompt)
        {
            var chatMessages = new List<ChatMessage>();
            
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                chatMessages.Add(new SystemChatMessage(systemPrompt));
            }

            foreach (var message in messages)
            {
                switch (message.Role)
                {
                    case LLMRole.User:
                        // 检查是否包含多模态内容
                        if (message.Contents.Any(c => c.Type == LLMContentType.Image))
                        {
                            var contentParts = new List<ChatMessageContentPart>();
                            
                            // 添加文本部分
                            if (!string.IsNullOrEmpty(message.Content))
                            {
                                contentParts.Add(ChatMessageContentPart.CreateTextPart(message.Content));
                            }
                            
                            // 添加图像部分
                            foreach (var content in message.Contents.Where(c => c.Type == LLMContentType.Image))
                            {
                                if (content.Image != null)
                                {
                                    byte[] imageBytes;
                                    if (!string.IsNullOrEmpty(content.Image.Data))
                                    {
                                        imageBytes = Convert.FromBase64String(content.Image.Data);
                                    }
                                    else if (!string.IsNullOrEmpty(content.Image.Url))
                                    {
                                        // 同步下载图像（在实际应用中可能需要异步处理）
                                        using var client = new HttpClient();
                                        imageBytes = client.GetByteArrayAsync(content.Image.Url).Result;
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                    
                                    contentParts.Add(ChatMessageContentPart.CreateImagePart(
                                        BinaryData.FromBytes(imageBytes), 
                                        content.Image.MimeType ?? "image/jpeg"));
                                }
                            }
                            
                            chatMessages.Add(new UserChatMessage(contentParts));
                        }
                        else
                        {
                            // 纯文本消息
                            chatMessages.Add(new UserChatMessage(message.Content));
                        }
                        break;
                    case LLMRole.Assistant:
                        chatMessages.Add(new AssistantChatMessage(message.Content));
                        break;
                    case LLMRole.System:
                        chatMessages.Add(new SystemChatMessage(message.Content));
                        break;
                }
            }

            return chatMessages;
        }

        private List<GenerativeAI.Types.Content> ConvertToGeminiHistory(List<LLMMessage> messages, string systemPrompt)
        {
            var history = new List<GenerativeAI.Types.Content>();

            foreach (var message in messages)
            {
                var role = message.Role switch
                {
                    LLMRole.User => Roles.User,
                    LLMRole.Assistant => Roles.Model,
                    LLMRole.System => Roles.User, // Gemini没有专门的system role
                    _ => Roles.User
                };

                history.Add(new GenerativeAI.Types.Content(message.Content, role));
            }

            return history;
        }

        private void ValidateRequest(LLMRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            
            if (string.IsNullOrWhiteSpace(request.Model))
                throw new ArgumentException("模型名称不能为空", nameof(request.Model));
            
            if (request.ChatHistory == null)
                throw new ArgumentException("聊天历史不能为null", nameof(request.ChatHistory));
            
            if (request.Channel == null)
                throw new ArgumentException("渠道配置不能为null", nameof(request.Channel));

            if (string.IsNullOrWhiteSpace(request.Channel.Gateway))
                throw new ArgumentException("渠道网关地址不能为空", nameof(request.Channel.Gateway));
        }
    }
} 
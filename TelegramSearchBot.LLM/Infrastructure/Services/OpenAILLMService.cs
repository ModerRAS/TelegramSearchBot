using System.Net;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;
using System.ClientModel.Primitives;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.LLM.Infrastructure.Services;

/// <summary>
/// OpenAI LLM服务实现
/// </summary>
public class OpenAILLMService : ILLMService
{
    private readonly ILogger<OpenAILLMService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenAILLMService(
        ILogger<OpenAILLMService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("执行OpenAI请求: Model={Model}, RequestId={RequestId}", 
                request.Model, request.RequestId);

            using var httpClient = CreateHttpClient(request.Channel);
            var chatClient = CreateChatClient(request, httpClient);
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

            var content = responseBuilder.ToString();
            _logger.LogInformation("OpenAI请求完成: RequestId={RequestId}, ContentLength={Length}", 
                request.RequestId, content.Length);

            return LLMResponse.Success(request.RequestId, request.Model, content, request.StartTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI请求失败: RequestId={RequestId}", request.RequestId);
            return LLMResponse.Failure(request.RequestId, request.Model, ex.Message, request.StartTime);
        }
    }

    public async Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(
        LLMRequest request, 
        CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<string>();
        var responseBuilder = new StringBuilder();

        var responseTask = Task.Run(async () =>
        {
            var llmResponse = LLMResponse.Streaming(request.RequestId, request.Model, request.StartTime);

            try
            {
                _logger.LogInformation("执行OpenAI流式请求: Model={Model}, RequestId={RequestId}", 
                    request.Model, request.RequestId);

                using var httpClient = CreateHttpClient(request.Channel);
                var chatClient = CreateChatClient(request, httpClient);
                var chatMessages = ConvertToOpenAIChatMessages(request.ChatHistory, request.SystemPrompt);

                await foreach (var update in chatClient.CompleteChatStreamingAsync(chatMessages, cancellationToken: cancellationToken))
                {
                    foreach (var updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>())
                    {
                        if (updatePart?.Text != null)
                        {
                            responseBuilder.Append(updatePart.Text);
                            await channel.Writer.WriteAsync(updatePart.Text, cancellationToken);
                        }
                    }
                }

                var content = responseBuilder.ToString();
                _logger.LogInformation("OpenAI流式请求完成: RequestId={RequestId}, ContentLength={Length}", 
                    request.RequestId, content.Length);

                return llmResponse with 
                { 
                    IsSuccess = true, 
                    Content = content, 
                    EndTime = DateTime.UtcNow 
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI流式请求失败: RequestId={RequestId}", request.RequestId);
                
                try 
                { 
                    await channel.Writer.WriteAsync($"错误: {ex.Message}", cancellationToken); 
                } 
                catch 
                { 
                    // 忽略写入错误
                }

                return llmResponse with 
                { 
                    IsSuccess = false, 
                    ErrorMessage = ex.Message, 
                    EndTime = DateTime.UtcNow 
                };
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);

        return (channel.Reader, responseTask);
    }

    public async Task<float[]> GenerateEmbeddingAsync(
        string text, 
        string model, 
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("生成OpenAI嵌入向量: Model={Model}, Gateway={Gateway}", model, channel.Gateway);
            
            using var httpClient = CreateHttpClient(channel);
            var clientOptions = CreateClientOptions(channel, httpClient);
            var client = new OpenAIClient(new ApiKeyCredential(channel.ApiKey), clientOptions);
            var embeddingClient = client.GetEmbeddingClient(model);
            
            var response = await embeddingClient.GenerateEmbeddingsAsync(new[] { text }, cancellationToken: cancellationToken);
            
            if (response?.Value != null && response.Value.Any())
            {
                var embedding = response.Value.First();
                _logger.LogDebug("Embedding response type: {Type}", embedding.GetType().FullName);
                
                // 尝试使用反射获取嵌入向量数据，支持多种可能的属性名
                var embeddingProp = embedding.GetType().GetProperty("Embedding")
                                  ?? embedding.GetType().GetProperty("EmbeddingVector")
                                  ?? embedding.GetType().GetProperty("Vector")
                                  ?? embedding.GetType().GetProperty("EmbeddingData")
                                  ?? embedding.GetType().GetProperty("Data");
                
                if (embeddingProp != null)
                {
                    var embeddingValue = embeddingProp.GetValue(embedding);
                    if (embeddingValue is float[] floatArray)
                    {
                        _logger.LogInformation("嵌入向量生成成功: Model={Model}, Dimension={Dimension}", model, floatArray.Length);
                        return floatArray;
                    }
                    else if (embeddingValue is IEnumerable<float> floatEnumerable)
                    {
                        var result = floatEnumerable.ToArray();
                        _logger.LogInformation("嵌入向量生成成功: Model={Model}, Dimension={Dimension}", model, result.Length);
                        return result;
                    }
                    else if (embeddingValue is IReadOnlyList<float> floatList)
                    {
                        var result = floatList.ToArray();
                        _logger.LogInformation("嵌入向量生成成功: Model={Model}, Dimension={Dimension}", model, result.Length);
                        return result;
                    }
                }
                
                // 最后尝试：查找任何 float[] 类型的属性
                var floatArrayProps = embedding.GetType().GetProperties()
                    .Where(p => p.PropertyType == typeof(float[]) || 
                               p.PropertyType == typeof(IEnumerable<float>) ||
                               p.PropertyType == typeof(IReadOnlyList<float>))
                    .ToList();
                
                if (floatArrayProps.Any())
                {
                    foreach (var prop in floatArrayProps)
                    {
                        var value = prop.GetValue(embedding);
                        if (value is float[] floats)
                        {
                            _logger.LogInformation("嵌入向量生成成功 (备用方式): Model={Model}, Dimension={Dimension}, Property={Property}", 
                                model, floats.Length, prop.Name);
                            return floats;
                        }
                        else if (value is IEnumerable<float> floatEnumerable)
                        {
                            var result = floatEnumerable.ToArray();
                            _logger.LogInformation("嵌入向量生成成功 (备用方式): Model={Model}, Dimension={Dimension}, Property={Property}", 
                                model, result.Length, prop.Name);
                            return result;
                        }
                    }
                }
                
                _logger.LogError("无法提取嵌入向量数据。可用属性: {Props}",
                    string.Join(", ", embedding.GetType().GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
            }
            
            _logger.LogError("OpenAI嵌入向量API返回空响应: Model={Model}", model);
            
            // 返回通用默认维度的空向量
            return new float[1536];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI嵌入向量生成异常: Model={Model}", model);
            
            // 发生异常时重新抛出，让调用方处理
            throw;
        }
    }

    public async Task<List<string>> GetAvailableModelsAsync(
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("获取OpenAI可用模型列表");

            using var httpClient = CreateHttpClient(channel);
            var clientOptions = CreateClientOptions(channel, httpClient);
            var client = new OpenAIClient(new ApiKeyCredential(channel.ApiKey), clientOptions);
            var modelClient = client.GetOpenAIModelClient();
            
            var modelsResponse = await modelClient.GetModelsAsync(cancellationToken);
            var models = modelsResponse.Value.Select(m => m.Id).ToList();
            
            _logger.LogInformation("获取到OpenAI模型列表: Count={Count}", models.Count);
            return models;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取OpenAI模型列表失败");
            return new List<string>();
        }
    }

    public async Task<bool> IsHealthyAsync(
        LLMChannelConfig channel, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("检查OpenAI服务健康状态");
            
            var models = await GetAvailableModelsAsync(channel, cancellationToken);
            var isHealthy = models.Any();
            
            _logger.LogInformation("OpenAI健康检查完成: IsHealthy={IsHealthy}", isHealthy);
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI健康检查失败");
            return false;
        }
    }

    private HttpClient CreateHttpClient(LLMChannelConfig channel)
    {
        var handler = new HttpClientHandler();
        
        if (!string.IsNullOrEmpty(channel.ProxyUrl))
        {
            handler.Proxy = new WebProxy(channel.ProxyUrl);
            handler.UseProxy = true;
        }

        var httpClient = new HttpClient(handler);
        httpClient.Timeout = TimeSpan.FromSeconds(channel.TimeoutSeconds);
        
        return httpClient;
    }

    private OpenAIClientOptions CreateClientOptions(LLMChannelConfig channel, HttpClient httpClient)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(channel.Gateway),
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        if (!string.IsNullOrEmpty(channel.OrganizationId))
        {
            clientOptions.OrganizationId = channel.OrganizationId;
        }

        return clientOptions;
    }

    private ChatClient CreateChatClient(LLMRequest request, HttpClient httpClient)
    {
        var clientOptions = CreateClientOptions(request.Channel, httpClient);
        return new ChatClient(
            model: request.Model, 
            credential: new ApiKeyCredential(request.Channel.ApiKey), 
            clientOptions);
    }

    private List<ChatMessage> ConvertToOpenAIChatMessages(List<LLMMessage> messages, string? systemPrompt)
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
                    if (HasImageContent(message))
                    {
                        var contentParts = CreateContentParts(message);
                        if (contentParts.Any())
                        {
                            chatMessages.Add(new UserChatMessage(contentParts));
                        }
                    }
                    else
                    {
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

    private bool HasImageContent(LLMMessage message)
    {
        return message.Contents.Any(c => c.Type == LLMContentType.Image);
    }

    private List<ChatMessageContentPart> CreateContentParts(LLMMessage message)
    {
        var contentParts = new List<ChatMessageContentPart>();
        
        if (!string.IsNullOrEmpty(message.Content))
        {
            contentParts.Add(ChatMessageContentPart.CreateTextPart(message.Content));
        }
        
        foreach (var content in message.Contents.Where(c => c.Type == LLMContentType.Image))
        {
            var imageBytes = GetImageBytes(content);
            if (imageBytes != null)
            {
                contentParts.Add(ChatMessageContentPart.CreateImagePart(
                    BinaryData.FromBytes(imageBytes), 
                    content.Image?.MimeType ?? "image/jpeg"));
            }
        }
        
        return contentParts;
    }

    private byte[]? GetImageBytes(LLMContent content)
    {
        if (content.Image == null) return null;

        try
        {
            if (!string.IsNullOrEmpty(content.Image.Data))
            {
                return Convert.FromBase64String(content.Image.Data);
            }
            
            if (!string.IsNullOrEmpty(content.Image.Url))
            {
                using var httpClient = _httpClientFactory.CreateClient();
                return httpClient.GetByteArrayAsync(content.Image.Url).Result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取图像数据失败");
        }

        return null;
    }
} 
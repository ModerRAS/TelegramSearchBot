using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM
{
    /// <summary>
    /// LLM核心适配器使用示例 - 展示如何使用统一的适配器接口处理多模态内容
    /// </summary>
    public class LLMCoreUsageExample
    {
        private readonly LLMCoreService _llmCoreService;
        private readonly ILogger<LLMCoreUsageExample> _logger;

        public LLMCoreUsageExample(
            LLMCoreService llmCoreService,
            ILogger<LLMCoreUsageExample> logger)
        {
            _llmCoreService = llmCoreService;
            _logger = logger;
        }

        /// <summary>
        /// 示例1：基础对话 - 非流式
        /// </summary>
        public async Task<string> BasicChatExample()
        {
            // 1. 构建聊天历史
            var chatHistory = new LLMChatHistoryBuilder()
                .AddSystemMessage("你是一个有用的AI助手，请用中文回答问题。")
                .AddUserMessage("你好，请介绍一下你自己。")
                .Build();

            // 2. 配置OpenAI渠道DTO
            var channelDto = new LLMChannelDto
            {
                Gateway = "https://api.openai.com/v1",
                ApiKey = "your-openai-api-key-here",
                Provider = LLMProvider.OpenAI,
                MaxConcurrency = 5,
                Priority = 1
            };

            // 3. 创建请求
            var request = new LLMRequest
            {
                Provider = LLMProvider.OpenAI,
                Model = "gpt-3.5-turbo",
                Channel = channelDto,
                ChatHistory = chatHistory,
                Options = new LLMGenerationOptions
                {
                    Temperature = 0.7,
                    MaxTokens = 1000
                }
            };

            // 4. 执行对话（非流式）
            var response = await _llmCoreService.ExecuteAsync(request);
            return response.IsSuccess ? response.Content : $"错误: {response.ErrorMessage}";
        }

        /// <summary>
        /// 示例2：使用Channel进行流式对话
        /// </summary>
        public async Task<string> StreamingChatWithChannelExample()
        {
            var chatHistory = new LLMChatHistoryBuilder()
                .AddSystemMessage("你是一个专业的技术助手。")
                .AddUserMessage("请解释什么是REST API？")
                .Build();

            var channelDto = new LLMChannelDto
            {
                Gateway = "http://localhost:11434",
                Provider = LLMProvider.Ollama,
                MaxConcurrency = 3
            };

            var request = new LLMRequest
            {
                Provider = LLMProvider.Ollama,
                Model = "llama3:latest",
                Channel = channelDto,
                ChatHistory = chatHistory
            };

            // 使用Channel进行流式处理
            var (streamReader, responseTask) = await _llmCoreService.ExecuteStreamAsync(request);
            var responseBuilder = new StringBuilder();

            // 实时读取流式内容
            await foreach (var chunk in streamReader.ReadAllAsync())
            {
                responseBuilder.Append(chunk);
                _logger.LogInformation("收到流式内容: {Chunk}", chunk);
            }

            // 等待最终响应
            var finalResponse = await responseTask;
            
            if (finalResponse.IsSuccess)
            {
                _logger.LogInformation("流式对话完成，总内容长度: {Length}", responseBuilder.Length);
                return responseBuilder.ToString();
            }
            else
            {
                return $"错误: {finalResponse.ErrorMessage}";
            }
        }

        /// <summary>
        /// 示例3：多模态对话 - 在统一接口中处理图像+文字
        /// </summary>
        public async Task<string> MultimodalChatExample(string imagePath)
        {
            // 读取图像文件
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var imageBase64 = Convert.ToBase64String(imageBytes);

            // 构建包含图像的消息 - 使用统一的聊天历史构建器
            var chatHistory = new LLMChatHistoryBuilder()
                .AddSystemMessage("你是一个图像分析专家，请仔细观察图像并详细描述。")
                .AddUserMessage("请分析这张图片", new LLMImageContent
                {
                    Data = imageBase64,
                    MimeType = "image/jpeg",
                    Description = "用户上传的图片"
                })
                .Build();

            var channelDto = new LLMChannelDto
            {
                Gateway = "https://api.openai.com/v1",
                ApiKey = "your-openai-api-key-here",
                Provider = LLMProvider.OpenAI
            };

            var request = new LLMRequest
            {
                Provider = LLMProvider.OpenAI,
                Model = "gpt-4-vision-preview",
                Channel = channelDto,
                ChatHistory = chatHistory,
                Options = new LLMGenerationOptions
                {
                    Temperature = 0.4,
                    MaxTokens = 2000
                }
            };

            // 使用统一的ExecuteAsync处理多模态内容
            var response = await _llmCoreService.ExecuteAsync(request);
            return response.IsSuccess ? response.Content : $"错误: {response.ErrorMessage}";
        }

        /// <summary>
        /// 示例4：多模态流式对话 - 实时处理包含图像的对话
        /// </summary>
        public async Task<string> MultimodalStreamingExample(string imagePath)
        {
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var imageBase64 = Convert.ToBase64String(imageBytes);

            var chatHistory = new LLMChatHistoryBuilder()
                .AddSystemMessage("你是一个视觉AI助手。")
                .AddUserMessage("请详细分析这张图片的内容", new LLMImageContent
                {
                    Data = imageBase64,
                    MimeType = "image/jpeg"
                })
                .Build();

            var channelDto = new LLMChannelDto
            {
                Gateway = "http://localhost:11434",
                Provider = LLMProvider.Ollama
            };

            var request = new LLMRequest
            {
                Provider = LLMProvider.Ollama,
                Model = "llava:latest",
                Channel = channelDto,
                ChatHistory = chatHistory
            };

            // 使用Channel进行多模态流式处理
            var (streamReader, responseTask) = await _llmCoreService.ExecuteStreamAsync(request);
            var responseBuilder = new StringBuilder();

            await foreach (var chunk in streamReader.ReadAllAsync())
            {
                responseBuilder.Append(chunk);
                _logger.LogInformation("多模态流式内容: {Chunk}", chunk);
            }

            var finalResponse = await responseTask;
            return finalResponse.IsSuccess ? responseBuilder.ToString() : $"错误: {finalResponse.ErrorMessage}";
        }

        /// <summary>
        /// 示例5：嵌入向量生成
        /// </summary>
        public async Task<float[]> EmbeddingExample()
        {
            const string text = "这是一个用于生成嵌入向量的示例文本。";
            
            var channelDto = new LLMChannelDto
            {
                Gateway = "https://api.openai.com/v1",
                ApiKey = "your-api-key-here",
                Provider = LLMProvider.OpenAI
            };

            try
            {
                var embedding = await _llmCoreService.GenerateEmbeddingAsync(
                    text, 
                    LLMProvider.OpenAI, 
                    "text-embedding-ada-002", 
                    channelDto
                );
                
                _logger.LogInformation("生成嵌入向量成功，维度: {Dimension}", embedding.Length);
                return embedding;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成嵌入向量失败");
                return new float[0];
            }
        }

        /// <summary>
        /// 示例6：混合模态对话历史 - 文本与图像混合
        /// </summary>
        public async Task<string> MixedModalityConversationExample(string[] imagePaths)
        {
            var chatHistory = new LLMChatHistoryBuilder()
                .AddSystemMessage("你是一个多模态AI助手，可以同时处理文本和图像。")
                .AddUserMessage("你好，我想要分析一些图片。");

            // 添加第一张图片
            if (imagePaths.Length > 0)
            {
                var image1Bytes = await File.ReadAllBytesAsync(imagePaths[0]);
                var image1Base64 = Convert.ToBase64String(image1Bytes);
                
                chatHistory.AddUserMessage("这是第一张图片，请描述一下", new LLMImageContent
                {
                    Data = image1Base64,
                    MimeType = "image/jpeg"
                });
            }

            // 添加助手回复
            chatHistory.AddAssistantMessage("我看到了第一张图片，这是一张很有趣的照片。");

            // 添加第二张图片
            if (imagePaths.Length > 1)
            {
                var image2Bytes = await File.ReadAllBytesAsync(imagePaths[1]);
                var image2Base64 = Convert.ToBase64String(image2Bytes);
                
                chatHistory.AddUserMessage("再看看这张图片，与第一张有什么区别？", new LLMImageContent
                {
                    Data = image2Base64,
                    MimeType = "image/jpeg"
                });
            }

            var channelDto = new LLMChannelDto
            {
                Gateway = "https://api.openai.com/v1",
                ApiKey = "your-api-key-here",
                Provider = LLMProvider.OpenAI
            };

            var request = new LLMRequest
            {
                Provider = LLMProvider.OpenAI,
                Model = "gpt-4-vision-preview",
                Channel = channelDto,
                ChatHistory = chatHistory.Build()
            };

            var response = await _llmCoreService.ExecuteAsync(request);
            return response.IsSuccess ? response.Content : $"错误: {response.ErrorMessage}";
        }

        /// <summary>
        /// 示例7：健康检查和模型列表获取
        /// </summary>
        public async Task<Dictionary<string, object>> ServiceInfoExample()
        {
            var result = new Dictionary<string, object>();

            // 配置多个渠道DTO
            var channelDtos = new[]
            {
                new LLMChannelDto
                {
                    Name = "OpenAI-GPT4",
                    Gateway = "https://api.openai.com/v1",
                    ApiKey = "your-openai-key",
                    Provider = LLMProvider.OpenAI
                },
                new LLMChannelDto
                {
                    Name = "Local-Ollama",
                    Gateway = "http://localhost:11434",
                    Provider = LLMProvider.Ollama
                },
                new LLMChannelDto
                {
                    Name = "Google-Gemini",
                    Gateway = "https://generativelanguage.googleapis.com/v1beta",
                    ApiKey = "your-gemini-key",
                    Provider = LLMProvider.Gemini
                }
            };

            foreach (var channelDto in channelDtos)
            {
                try
                {
                    // 健康检查
                    var isHealthy = await _llmCoreService.IsHealthyAsync(channelDto.Provider, channelDto);
                    
                    // 获取可用模型
                    var models = await _llmCoreService.GetAvailableModelsAsync(channelDto.Provider, channelDto);

                    result[channelDto.Name] = new
                    {
                        IsHealthy = isHealthy,
                        Provider = channelDto.Provider.ToString(),
                        Gateway = channelDto.Gateway,
                        AvailableModels = models,
                        ModelsCount = models.Count
                    };

                    _logger.LogInformation("渠道 {ChannelName} 状态: 健康={IsHealthy}, 模型数量={ModelsCount}",
                        channelDto.Name, isHealthy, models.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "检查渠道 {ChannelName} 状态失败", channelDto.Name);
                    result[channelDto.Name] = new
                    {
                        IsHealthy = false,
                        Error = ex.Message
                    };
                }
            }

            return result;
        }

        /// <summary>
        /// 示例8：高级流式处理 - 实时处理和取消
        /// </summary>
        public async Task<string> AdvancedStreamingExample(CancellationToken cancellationToken = default)
        {
            var chatHistory = new LLMChatHistoryBuilder()
                .AddSystemMessage("请用讲故事的方式回答问题，分段展示。")
                .AddUserMessage("请给我讲一个关于人工智能的未来的故事。")
                .Build();

            var channelDto = new LLMChannelDto
            {
                Gateway = "https://api.openai.com/v1",
                ApiKey = "your-api-key-here",
                Provider = LLMProvider.OpenAI
            };

            var request = new LLMRequest
            {
                Provider = LLMProvider.OpenAI,
                Model = "gpt-3.5-turbo",
                Channel = channelDto,
                ChatHistory = chatHistory,
                Options = new LLMGenerationOptions
                {
                    Temperature = 0.9,
                    MaxTokens = 2000,
                    Stream = true
                }
            };

            // 使用Channel进行高级流式处理
            var (streamReader, responseTask) = await _llmCoreService.ExecuteStreamAsync(request, cancellationToken);
            var responseBuilder = new StringBuilder();
            var sentenceCount = 0;

            try
            {
                // 实时处理每个内容片段
                await foreach (var chunk in streamReader.ReadAllAsync(cancellationToken))
                {
                    responseBuilder.Append(chunk);
                    
                    // 检测句子结束
                    if (chunk.Contains("。") || chunk.Contains("！") || chunk.Contains("？"))
                    {
                        sentenceCount++;
                        _logger.LogDebug("检测到第 {Count} 个句子结束", sentenceCount);
                        
                        // 可以在这里实现更多实时处理逻辑
                        // 比如实时发送给客户端、语音合成等
                    }
                    
                    // 模拟处理延迟或用户取消
                    if (sentenceCount >= 5)
                    {
                        _logger.LogInformation("达到句子数量限制，提前结束");
                        break;
                    }
                }

                // 等待最终响应（或者已经被取消）
                var finalResponse = await responseTask;
                
                _logger.LogInformation("流式处理完成，句子数: {SentenceCount}, 总长度: {Length}",
                    sentenceCount, responseBuilder.Length);
                    
                return responseBuilder.ToString();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("流式处理被用户取消");
                return responseBuilder.ToString() + "\n[已取消]";
            }
        }

        /// <summary>
        /// 示例9：从数据库LLMChannel转换使用
        /// </summary>
        public async Task<string> DatabaseChannelExample(TelegramSearchBot.Model.Data.LLMChannel dbChannel)
        {
            // 从数据库模型转换为DTO
            var channelDto = LLMChannelDto.FromDataModel(dbChannel);
            
            if (channelDto == null || !channelDto.IsAvailable)
            {
                return "渠道不可用";
            }

            var chatHistory = new LLMChatHistoryBuilder()
                .AddSystemMessage("你是一个有用的助手。")
                .AddUserMessage("测试消息")
                .Build();

            var request = new LLMRequest
            {
                Provider = channelDto.Provider,
                Model = "default-model", // 实际应用中应该从数据库获取
                Channel = channelDto,
                ChatHistory = chatHistory
            };

            var response = await _llmCoreService.ExecuteAsync(request);
            return response.IsSuccess ? response.Content : $"错误: {response.ErrorMessage}";
        }

        /// <summary>
        /// 示例10：复杂多模态场景 - 文档+图像分析
        /// </summary>
        public async Task<string> ComplexMultimodalExample(string documentPath, string imagePath)
        {
            // 读取文档内容
            var documentContent = await File.ReadAllTextAsync(documentPath);
            
            // 读取图像
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var imageBase64 = Convert.ToBase64String(imageBytes);

            var chatHistory = new LLMChatHistoryBuilder()
                .AddSystemMessage("你是一个专业的文档和图像分析专家。")
                .AddUserMessage($"我有一份文档内容：\n{documentContent}")
                .AddAssistantMessage("我已经阅读了您的文档内容。")
                .AddUserMessage("现在请结合这张图片分析，看看图片内容是否与文档相符", new LLMImageContent
                {
                    Data = imageBase64,
                    MimeType = "image/jpeg"
                })
                .Build();

            var channelDto = new LLMChannelDto
            {
                Gateway = "https://api.openai.com/v1",
                ApiKey = "your-api-key-here", 
                Provider = LLMProvider.OpenAI
            };

            var request = new LLMRequest
            {
                Provider = LLMProvider.OpenAI,
                Model = "gpt-4-vision-preview",
                Channel = channelDto,
                ChatHistory = chatHistory,
                Options = new LLMGenerationOptions
                {
                    Temperature = 0.3,
                    MaxTokens = 3000
                }
            };

            var response = await _llmCoreService.ExecuteAsync(request);
            return response.IsSuccess ? response.Content : $"错误: {response.ErrorMessage}";
        }
    }
} 
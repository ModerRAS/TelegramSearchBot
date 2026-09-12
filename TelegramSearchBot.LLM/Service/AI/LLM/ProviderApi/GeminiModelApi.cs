using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenerativeAI;
using GenerativeAI.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SkiaSharp;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Transient)]
    public class GeminiModelApi : ILlmModelCatalog, ILlmEmbeddings, ILlmVision {
        private const string ServiceName = "GeminiModelApi";
        private readonly ILogger<GeminiModelApi> _logger;
        private readonly DataDbContext _dbContext;
        private readonly Dictionary<long, ChatSession> _chatSessions = new();
        private readonly IHttpClientFactory _httpClientFactory;

        public GeminiModelApi(
            DataDbContext context,
            ILogger<GeminiModelApi> logger,
            IHttpClientFactory httpClientFactory) {
            _logger = logger;
            _dbContext = context;
            _httpClientFactory = httpClientFactory;
            _logger.LogInformation("GeminiModelApi instance created");
        }


        private void AddMessageToHistory(List<GenerativeAI.Types.Content> chatHistory, long fromUserId, string content) {
            AddMessageToHistory(chatHistory, fromUserId, content, null);
        }

        private void AddMessageToHistory(List<GenerativeAI.Types.Content> chatHistory, long fromUserId, string content, List<byte[]> images) {
            if (string.IsNullOrWhiteSpace(content) && ( images == null || images.Count == 0 )) return;
            if (!string.IsNullOrWhiteSpace(content)) {
                content = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\n{3,}", "\n\n");
            }

            var role = fromUserId == Env.BotId ? Roles.Model : Roles.User;

            if (images != null && images.Count > 0 && role == Roles.User) {
                var parts = new List<Part>();
                if (!string.IsNullOrWhiteSpace(content)) {
                    parts.Add(new Part { Text = content.Trim() });
                }
                foreach (var imageBytes in images) {
                    parts.Add(new Part {
                        InlineData = new GenerativeAI.Types.Blob {
                            MimeType = "image/png",
                            Data = Convert.ToBase64String(imageBytes)
                        }
                    });
                }
                chatHistory.Add(new Content { Parts = parts, Role = role });
            } else {
                chatHistory.Add(new Content(content?.Trim() ?? "", role));
            }
        }


        /// <summary>
        /// 尝试加载消息关联的图片文件，转换为PNG格式的字节数组
        /// </summary>
        private byte[] TryLoadMessagePhoto(long chatId, long messageId) {
            try {
                var dirPath = Path.Combine(Env.WorkDir, "Photos", $"{chatId}");
                if (!Directory.Exists(dirPath)) return null;

                var files = Directory.GetFiles(dirPath, $"{messageId}.*");
                if (files.Length == 0) return null;

                var filePath = files[0];
                using var fileStream = File.OpenRead(filePath);
                var bitmap = SKBitmap.Decode(fileStream);
                if (bitmap == null) return null;

                var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 90);
                return encoded?.ToArray();
            } catch (Exception ex) {
                _logger.LogDebug(ex, "无法加载消息图片: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
                return null;
            }
        }

        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            if (channel.Provider.Equals(LLMProvider.Ollama)) {
                return new List<string>();
            }

            try {
                var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
                var modelsResponse = await googleAI.ListModelsAsync();
                return modelsResponse.Models.Select(m => m.Name.Replace("models/", ""));
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to list Gemini models");
                return new List<string>();
            }
        }

        /// <summary>
        /// 获取Gemini模型及其能力信息
        /// </summary>
        public virtual async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel) {
            if (channel.Provider.Equals(LLMProvider.Ollama)) {
                return new List<ModelWithCapabilities>();
            }

            try {
                var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
                var modelsResponse = await googleAI.ListModelsAsync();
                var results = new List<ModelWithCapabilities>();

                foreach (var model in modelsResponse.Models) {
                    var modelName = model.Name.Replace("models/", "");
                    var modelWithCaps = InferGeminiModelCapabilities(modelName, model);
                    results.Add(modelWithCaps);
                }

                _logger.LogInformation("Retrieved {Count} Gemini models with capabilities", results.Count);
                return results;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to list Gemini models with capabilities");
                return new List<ModelWithCapabilities>();
            }
        }

        /// <summary>
        /// 根据Gemini模型名称和信息推断能力
        /// </summary>
        private ModelWithCapabilities InferGeminiModelCapabilities(string modelName, GenerativeAI.Types.Model modelInfo) {
            var model = new ModelWithCapabilities { ModelName = modelName };
            var lowerName = modelName.ToLower();

            // 基本能力设置
            model.SetCapability("streaming", true); // Gemini API支持流式响应

            // 从Gemini API模型信息中获取支持的方法
            if (modelInfo.SupportedGenerationMethods != null) {
                foreach (var method in modelInfo.SupportedGenerationMethods) {
                    if (method.ToLower().Contains("generatecontent")) {
                        model.SetCapability("chat", true);
                    } else if (method.ToLower().Contains("embed")) {
                        model.SetCapability("embedding", true);
                    }
                }
            }

            // 基于模型名称推断能力
            if (lowerName.Contains("gemini")) {
                // Gemini模型系列能力
                model.SetCapability("function_calling", true);
                model.SetCapability("tool_calls", true);
                model.SetCapability("response_json_object", true);

                // Gemini 2.0和Pro模型支持更多功能
                if (lowerName.Contains("2.0") || lowerName.Contains("pro")) {
                    model.SetCapability("vision", true);
                    model.SetCapability("multimodal", true);
                    model.SetCapability("image_content", true);
                    model.SetCapability("audio_content", true);
                    model.SetCapability("video_content", true);
                    model.SetCapability("file_upload", true);
                }
                // Gemini 1.5系列
                else if (lowerName.Contains("1.5")) {
                    model.SetCapability("vision", true);
                    model.SetCapability("multimodal", true);
                    model.SetCapability("image_content", true);

                    if (lowerName.Contains("pro")) {
                        model.SetCapability("long_context", true);
                        model.SetCapability("file_upload", true);
                        model.SetCapability("audio_content", true);
                    }
                }
                // Flash模型 - 更快的响应
                if (lowerName.Contains("flash")) {
                    model.SetCapability("fast_response", true);
                    model.SetCapability("optimized", true);
                }

                // Pro模型 - 更强的能力
                if (lowerName.Contains("pro")) {
                    model.SetCapability("advanced_reasoning", true);
                    model.SetCapability("complex_tasks", true);
                }
            }

            // 嵌入模型检测
            if (lowerName.Contains("embedding") || lowerName.Contains("embed")) {
                model.SetCapability("embedding", true);
                model.SetCapability("text_embedding", true);
                model.SetCapability("function_calling", false);
                model.SetCapability("vision", false);
                model.SetCapability("chat", false);
            }

            // 文本生成模型
            if (lowerName.Contains("text") && !lowerName.Contains("embedding")) {
                model.SetCapability("text_generation", true);
                model.SetCapability("chat", true);
            }

            // 从模型信息中提取输入/输出token限制
            if (modelInfo.InputTokenLimit > 0) {
                model.SetCapability("input_token_limit", modelInfo.InputTokenLimit.ToString());
            }

            if (modelInfo.OutputTokenLimit > 0) {
                model.SetCapability("output_token_limit", modelInfo.OutputTokenLimit.ToString());
            }

            // 设置模型版本信息
            model.SetCapability("model_version", modelInfo.Version ?? "unknown");
            model.SetCapability("model_family", "Gemini");

            // 基于模型名称的特殊能力
            if (lowerName.Contains("code")) {
                model.SetCapability("code_generation", true);
                model.SetCapability("code_completion", true);
            }

            return model;
        }




        /// <inheritdoc />
        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel) {
            if (channel == null || string.IsNullOrWhiteSpace(channel.ApiKey)) {
                _logger.LogError("{ServiceName}: Channel or ApiKey is not configured", ServiceName);
                throw new ArgumentException("Channel or ApiKey is not configured");
            }

            try {
                var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
                var embeddings = googleAI.CreateEmbeddingModel("models/embedding-001");
                var response = await embeddings.EmbedContentAsync(text);
#pragma warning disable CS8602 // 解引用可能出现空引用。
                return response.Embedding.Values.Select(v => ( float ) v).ToArray();
#pragma warning restore CS8602 // 解引用可能出现空引用。
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to generate embeddings");
                throw;
            }
        }

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, string prompt = null) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                modelName = "gpt-4-vision-preview";
            }

            prompt = string.IsNullOrWhiteSpace(prompt) ? GeneralLLMService.DefaultAltPhotoPrompt : prompt;

            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway) || string.IsNullOrWhiteSpace(channel.ApiKey)) {
                _logger.LogError("{ServiceName}: Channel, Gateway or ApiKey is not configured.", ServiceName);
                return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
            }

            var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
            var model = googleAI.CreateGenerativeModel("models/" + modelName);
            try {
                var chat = model.StartChat();


                var request = new GenerateContentRequest();
                request.AddText(prompt);
                // Attach a local file
                request.AddInlineFile(photoPath);
                // Generate the content with attached files
                var response = await chat.GenerateContentAsync(request);
                return response.Text;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error analyzing image with OpenAI");
                return $"Error analyzing image: {ex.Message}";
            }
        }
    }
}

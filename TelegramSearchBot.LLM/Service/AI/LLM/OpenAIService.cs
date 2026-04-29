using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http; // Added for IHttpClientFactory
using System.Reflection;
using System.Text;
using System.Threading; // For CancellationToken
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using SkiaSharp; // Added for image processing
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Tools; // For BraveSearchResult
// Using alias for the common internal ChatMessage format
using CommonChat = OpenAI.Chat;

namespace TelegramSearchBot.Service.AI.LLM {
    // Standalone implementation, not inheriting from BaseLlmService
    [Injectable(ServiceLifetime.Transient)]
    public class OpenAIService : IService, ILLMService {
        public string ServiceName => "OpenAIService";

        /// <summary>
        /// Mutable accumulator for streaming tool call updates.
        /// </summary>
        private class ToolCallAccumulator {
            public string Id { get; set; }
            public string Name { get; set; }
            public StringBuilder Arguments { get; } = new StringBuilder();
        }

        private readonly ILogger<OpenAIService> _logger;
        public static string _botName;
        public string BotName {
            get {
                return _botName;
            }
            set {
                _botName = value;
            }
        }
        private readonly DataDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMessageExtensionService _messageExtensionService;

        public OpenAIService(
            DataDbContext context,
            ILogger<OpenAIService> logger,
            IMessageExtensionService messageExtensionService,
            IHttpClientFactory httpClientFactory) {
            _logger = logger;
            _dbContext = context;
            _messageExtensionService = messageExtensionService;
            _httpClientFactory = httpClientFactory;
            _logger.LogInformation("OpenAIService instance created. McpToolHelper should be initialized at application startup.");
        }

        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            if (channel.Provider.Equals(LLMProvider.Ollama)) {
                return new List<string>();
            }

            // MiniMax 使用预定义模型列表
            if (channel.Provider == LLMProvider.MiniMax) {
                return _miniMaxModels;
            }

            // 检查是否为OpenRouter
            if (IsOpenRouter(channel.Gateway)) {
                return await GetOpenRouterModels(channel);
            }

            // 首先尝试使用通用 HTTP 方式获取模型列表（兼容 MiniMax 等非标准 OpenAI 兼容 API）
            var genericModels = await GetGenericOpenAICompatibleModels(channel);
            if (genericModels.Any()) {
                return genericModels;
            }

            // 回退到 OpenAI SDK
            try {
                var handler = new HttpClientHandler {
                    Proxy = WebRequest.DefaultWebProxy,
                    UseProxy = true
                };

                using var httpClient = new HttpClient(handler);

                // --- Client Setup ---
                var clientOptions = new OpenAIClientOptions {
                    Endpoint = new Uri(channel.Gateway),
                    Transport = new HttpClientPipelineTransport(httpClient),
                };

                var apikey = new ApiKeyCredential(channel.ApiKey);

                OpenAIClient client = new(apikey, clientOptions);
                var model = client.GetOpenAIModelClient();
                var models = await model.GetModelsAsync();
                return from s in models.Value
                       select s.Id;
            } catch (Exception ex) {
                _logger.LogError(ex, "使用 OpenAI SDK 获取模型列表失败 (Gateway: {Gateway})", channel.Gateway);
                return new List<string>();
            }
        }

        /// <summary>
        /// 使用通用 HTTP GET 方式获取 OpenAI 兼容 API 的模型列表，兼容 MiniMax、DeepSeek 等提供商
        /// </summary>
        private async Task<IEnumerable<string>> GetGenericOpenAICompatibleModels(LLMChannel channel) {
            try {
                var httpClient = _httpClientFactory.CreateClient();
                if (!string.IsNullOrEmpty(channel.ApiKey)) {
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {channel.ApiKey}");
                }

                // 构建模型列表 URL，确保路径正确
                var gatewayBase = channel.Gateway.TrimEnd('/');
                var modelsUrl = gatewayBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                    ? $"{gatewayBase}/models"
                    : $"{gatewayBase}/v1/models";

                var response = await httpClient.GetAsync(modelsUrl);
                if (!response.IsSuccessStatusCode) {
                    // 尝试不带 /v1 的路径
                    var altUrl = $"{gatewayBase}/models";
                    if (altUrl != modelsUrl) {
                        response = await httpClient.GetAsync(altUrl);
                    }
                }

                if (response.IsSuccessStatusCode) {
                    var content = await response.Content.ReadAsStringAsync();
                    var modelsData = JsonConvert.DeserializeObject<dynamic>(content);

                    var models = new List<string>();
                    if (modelsData?.data != null) {
                        foreach (var model in modelsData.data) {
                            string modelId = model.id?.ToString();
                            if (!string.IsNullOrEmpty(modelId)) {
                                models.Add(modelId);
                            }
                        }
                    }

                    if (models.Any()) {
                        _logger.LogInformation("通用 HTTP 方式获取到 {Count} 个模型 (Gateway: {Gateway})", models.Count, channel.Gateway);
                    }
                    return models;
                } else {
                    _logger.LogDebug("通用 HTTP 方式获取模型列表失败: {StatusCode} (Gateway: {Gateway})", response.StatusCode, channel.Gateway);
                    return new List<string>();
                }
            } catch (Exception ex) {
                _logger.LogDebug(ex, "通用 HTTP 方式获取模型列表出错 (Gateway: {Gateway})", channel.Gateway);
                return new List<string>();
            }
        }

        /// <summary>
        /// 检查是否为OpenRouter服务
        /// </summary>
        private bool IsOpenRouter(string gateway) {
            return !string.IsNullOrEmpty(gateway) &&
                   ( gateway.Contains("openrouter.ai") || gateway.Contains("openrouter") );
        }

        /// <summary>
        /// MiniMax预定义模型列表
        /// </summary>
        private static readonly string[] _miniMaxModels = {
            "MiniMax-M2.5",
            "MiniMax-M2.5-highspeed",
            "MiniMax-M2.1",
            "MiniMax-M2.1-highspeed",
            "MiniMax-M2"
        };

        /// <summary>
        /// 获取OpenRouter模型列表
        /// </summary>
        private async Task<IEnumerable<string>> GetOpenRouterModels(LLMChannel channel) {
            try {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {channel.ApiKey}");

                var response = await httpClient.GetAsync("https://openrouter.ai/api/v1/models");
                if (response.IsSuccessStatusCode) {
                    var content = await response.Content.ReadAsStringAsync();
                    var modelsData = JsonConvert.DeserializeObject<dynamic>(content);

                    var models = new List<string>();
                    if (modelsData?.data != null) {
                        foreach (var model in modelsData.data) {
                            string modelId = model.id?.ToString();
                            if (!string.IsNullOrEmpty(modelId)) {
                                models.Add(modelId);
                            }
                        }
                    }

                    _logger.LogInformation("获取到 {Count} 个OpenRouter模型", models.Count);
                    return models;
                } else {
                    _logger.LogWarning("获取OpenRouter模型失败: {StatusCode}", response.StatusCode);
                    return new List<string>();
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "获取OpenRouter模型时出错");
                return new List<string>();
            }
        }

        /// <summary>
        /// 获取OpenAI模型及其能力信息
        /// </summary>
        public virtual async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel) {
            if (channel.Provider.Equals(LLMProvider.Ollama)) {
                return new List<ModelWithCapabilities>();
            }

            // 检查是否为OpenRouter
            if (IsOpenRouter(channel.Gateway)) {
                return await GetOpenRouterModelsWithCapabilities(channel);
            }

            // 检查是否为LMStudio（通过capabilities字段检测）
            if (channel.Provider == LLMProvider.LMStudio) {
                return await GetLMStudioModelsWithCapabilities(channel);
            }


            using var httpClient = _httpClientFactory.CreateClient();

            try {
                // 尝试使用OpenAI内部API获取模型能力信息
                var internalApiUrl = channel.Gateway.TrimEnd('/') + "/dashboard/onboarding/models";
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {channel.ApiKey}");

                var response = await httpClient.GetAsync(internalApiUrl);
                if (response.IsSuccessStatusCode) {
                    var content = await response.Content.ReadAsStringAsync();
                    var modelsWithCapabilities = ParseOpenAIModelsWithCapabilities(content);
                    if (modelsWithCapabilities.Any()) {
                        _logger.LogInformation("Successfully retrieved {Count} OpenAI models with capabilities from internal API", modelsWithCapabilities.Count());
                        return modelsWithCapabilities;
                    }
                }

                _logger.LogInformation("Internal API failed, falling back to standard models API with hardcoded capabilities");

                // 如果内部API失败，使用标准API并根据模型名称推断能力
                var clientOptions = new OpenAIClientOptions {
                    Endpoint = new Uri(channel.Gateway),
                    Transport = new HttpClientPipelineTransport(httpClient),
                };

                var apikey = new ApiKeyCredential(channel.ApiKey);
                OpenAIClient client = new(apikey, clientOptions);
                var model = client.GetOpenAIModelClient();
                var models = await model.GetModelsAsync();

                return models.Value.Select(m => InferOpenAIModelCapabilities(m.Id));
            } catch (Exception ex) {
                _logger.LogError(ex, "Error getting OpenAI models with capabilities");
                return new List<ModelWithCapabilities>();
            }
        }

        /// <summary>
        /// 解析OpenAI内部API返回的模型能力信息
        /// </summary>
        private IEnumerable<ModelWithCapabilities> ParseOpenAIModelsWithCapabilities(string jsonContent) {
            try {
                var modelsData = JsonConvert.DeserializeObject<dynamic>(jsonContent);
                var results = new List<ModelWithCapabilities>();

                if (modelsData?.data != null) {
                    foreach (var modelData in modelsData.data) {
                        var modelWithCaps = new ModelWithCapabilities {
                            ModelName = modelData.id?.ToString() ?? ""
                        };

                        // 解析features数组
                        if (modelData.features != null) {
                            foreach (var feature in modelData.features) {
                                string featureName = feature?.ToString() ?? "";
                                modelWithCaps.SetCapability(featureName, true);
                            }
                        }

                        // 解析其他能力字段
                        if (modelData.capabilities != null) {
                            foreach (var capability in modelData.capabilities) {
                                string capName = capability.Name?.ToString() ?? "";
                                string capValue = capability.Value?.ToString() ?? "";
                                modelWithCaps.SetCapability(capName, capValue);
                            }
                        }

                        results.Add(modelWithCaps);
                    }
                }

                return results;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error parsing OpenAI models capabilities JSON");
                return new List<ModelWithCapabilities>();
            }
        }

        /// <summary>
        /// 根据OpenAI模型名称推断能力
        /// </summary>
        private ModelWithCapabilities InferOpenAIModelCapabilities(string modelName) {
            var model = new ModelWithCapabilities { ModelName = modelName };

            // 基于模型名称的能力推断
            var lowerName = modelName.ToLower();

            // 嵌入模型
            if (lowerName.Contains("embedding") || lowerName.Contains("ada")) {
                model.SetCapability("embedding", true);
                model.SetCapability("function_calling", false);
                model.SetCapability("vision", false);
            }
            // GPT-4系列模型
            else if (lowerName.StartsWith("gpt-4")) {
                model.SetCapability("function_calling", true);
                model.SetCapability("streaming", true);
                model.SetCapability("response_json_object", true);

                // GPT-4 Vision模型
                if (lowerName.Contains("vision") || lowerName.Contains("4o") || lowerName.Contains("4-turbo")) {
                    model.SetCapability("vision", true);
                    model.SetCapability("image_content", true);
                    model.SetCapability("multimodal", true);
                }

                // 较新的模型支持并行工具调用
                if (lowerName.Contains("4o") || lowerName.Contains("4-turbo") || lowerName.Contains("1106") || lowerName.Contains("0125")) {
                    model.SetCapability("parallel_tool_calls", true);
                    model.SetCapability("response_json_schema", true);
                }
            }
            // GPT-3.5系列模型
            else if (lowerName.StartsWith("gpt-3.5")) {
                model.SetCapability("function_calling", true);
                model.SetCapability("streaming", true);

                if (lowerName.Contains("1106") || lowerName.Contains("0125")) {
                    model.SetCapability("response_json_object", true);
                }
            }
            // DALL-E模型
            else if (lowerName.Contains("dall-e")) {
                model.SetCapability("image_generation", true);
                model.SetCapability("function_calling", false);
            }
            // Whisper模型
            else if (lowerName.Contains("whisper")) {
                model.SetCapability("audio_transcription", true);
                model.SetCapability("function_calling", false);
            }
            // TTS模型
            else if (lowerName.Contains("tts")) {
                model.SetCapability("text_to_speech", true);
                model.SetCapability("function_calling", false);
            }

            return model;
        }

        /// <summary>
        /// 获取OpenRouter模型及其能力信息
        /// </summary>
        private async Task<IEnumerable<ModelWithCapabilities>> GetOpenRouterModelsWithCapabilities(LLMChannel channel) {
            try {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {channel.ApiKey}");

                var response = await httpClient.GetAsync("https://openrouter.ai/api/v1/models");
                if (response.IsSuccessStatusCode) {
                    var content = await response.Content.ReadAsStringAsync();
                    var modelsData = JsonConvert.DeserializeObject<dynamic>(content);

                    var results = new List<ModelWithCapabilities>();
                    if (modelsData?.data != null) {
                        foreach (var modelData in modelsData.data) {
                            var modelWithCaps = ParseOpenRouterModelCapabilities(modelData);
                            if (modelWithCaps != null) {
                                results.Add(modelWithCaps);
                            }
                        }
                    }

                    _logger.LogInformation("获取到 {Count} 个OpenRouter模型及其能力信息", results.Count);
                    return results;
                } else {
                    _logger.LogWarning("获取OpenRouter模型能力失败: {StatusCode}", response.StatusCode);
                    return new List<ModelWithCapabilities>();
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "获取OpenRouter模型能力时出错");
                return new List<ModelWithCapabilities>();
            }
        }

        /// <summary>
        /// 解析OpenRouter模型能力信息
        /// </summary>
        private ModelWithCapabilities ParseOpenRouterModelCapabilities(dynamic modelData) {
            try {
                string modelId = modelData.id?.ToString();
                if (string.IsNullOrEmpty(modelId)) {
                    return null;
                }

                var model = new ModelWithCapabilities { ModelName = modelId };

                // 基本信息
                if (modelData.name != null) {
                    model.SetCapability("display_name", modelData.name.ToString());
                }

                if (modelData.description != null) {
                    model.SetCapability("description", modelData.description.ToString());
                }

                if (modelData.context_length != null) {
                    model.SetCapability("context_length", modelData.context_length.ToString());
                }

                // 架构信息
                if (modelData.architecture != null) {
                    var architecture = modelData.architecture;

                    // 输入模态
                    if (architecture.input_modalities != null) {
                        bool supportsText = false;
                        bool supportsImage = false;

                        foreach (var modality in architecture.input_modalities) {
                            string modalityStr = modality.ToString().ToLower();
                            if (modalityStr == "text") {
                                supportsText = true;
                            } else if (modalityStr == "image") {
                                supportsImage = true;
                            }
                        }

                        model.SetCapability("text_input", supportsText);
                        model.SetCapability("vision", supportsImage);
                        model.SetCapability("image_content", supportsImage);
                        model.SetCapability("multimodal", supportsImage);
                    }

                    // 输出模态
                    if (architecture.output_modalities != null) {
                        foreach (var modality in architecture.output_modalities) {
                            string modalityStr = modality.ToString().ToLower();
                            if (modalityStr == "text") {
                                model.SetCapability("text_output", true);
                            }
                        }
                    }

                    if (architecture.tokenizer != null) {
                        model.SetCapability("tokenizer", architecture.tokenizer.ToString());
                    }
                }

                // 定价信息
                if (modelData.pricing != null) {
                    var pricing = modelData.pricing;
                    if (pricing.prompt != null) {
                        model.SetCapability("prompt_price", pricing.prompt.ToString());
                    }
                    if (pricing.completion != null) {
                        model.SetCapability("completion_price", pricing.completion.ToString());
                    }
                    if (pricing.image != null) {
                        model.SetCapability("image_price", pricing.image.ToString());
                    }
                }

                // 支持的参数
                if (modelData.supported_parameters != null) {
                    var supportedParams = new List<string>();
                    foreach (var param in modelData.supported_parameters) {
                        string paramStr = param.ToString();
                        supportedParams.Add(paramStr);

                        // 检查工具调用支持
                        if (paramStr.ToLower().Contains("tool") || paramStr.ToLower().Contains("function")) {
                            model.SetCapability("function_calling", true);
                            model.SetCapability("tool_calls", true);
                        }

                        // 检查流式支持
                        if (paramStr.ToLower().Contains("stream")) {
                            model.SetCapability("streaming", true);
                        }

                        // 检查JSON格式支持
                        if (paramStr.ToLower().Contains("response_format")) {
                            model.SetCapability("response_json_object", true);
                        }
                    }

                    model.SetCapability("supported_parameters", string.Join(", ", supportedParams));
                }

                // 基于模型名称的额外推断
                var lowerModelId = modelId.ToLower();

                // 推断提供商
                if (lowerModelId.Contains("openai/") || lowerModelId.Contains("gpt")) {
                    model.SetCapability("provider", "OpenAI");
                } else if (lowerModelId.Contains("anthropic/") || lowerModelId.Contains("claude")) {
                    model.SetCapability("provider", "Anthropic");
                } else if (lowerModelId.Contains("google/") || lowerModelId.Contains("gemini")) {
                    model.SetCapability("provider", "Google");
                } else if (lowerModelId.Contains("meta/") || lowerModelId.Contains("llama")) {
                    model.SetCapability("provider", "Meta");
                } else if (lowerModelId.Contains("mistral/")) {
                    model.SetCapability("provider", "Mistral");
                }

                // 默认能力设置
                model.SetCapability("chat", true);

                // 如果没有明确的工具调用信息，基于模型名称推断
                if (!model.GetCapabilityBool("function_calling")) {
                    if (lowerModelId.Contains("gpt-4") || lowerModelId.Contains("gpt-3.5") ||
                        lowerModelId.Contains("claude") || lowerModelId.Contains("gemini")) {
                        model.SetCapability("function_calling", true);
                        model.SetCapability("tool_calls", true);
                    }
                }

                return model;
            } catch (Exception ex) {
                string modelDataStr = modelData?.ToString() ?? "null";
                _logger.LogError(ex, "解析OpenRouter模型能力时出错: {ModelData}", modelDataStr);
                return null;
            }
        }

        // --- LMStudio Capability Detection ---

        /// <summary>
        /// 获取LMStudio模型及其能力信息
        /// LMStudio在/v1/models返回的模型对象中包含capabilities数组
        /// </summary>
        private async Task<IEnumerable<ModelWithCapabilities>> GetLMStudioModelsWithCapabilities(LLMChannel channel) {
            try {
                var httpClient = _httpClientFactory.CreateClient();
                if (!string.IsNullOrEmpty(channel.ApiKey)) {
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {channel.ApiKey}");
                }

                var gatewayBase = channel.Gateway.TrimEnd('/');
                var modelsUrl = gatewayBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                    ? $"{gatewayBase}/models"
                    : $"{gatewayBase}/v1/models";

                var response = await httpClient.GetAsync(modelsUrl);
                if (response.IsSuccessStatusCode) {
                    var content = await response.Content.ReadAsStringAsync();
                    var modelsData = JsonConvert.DeserializeObject<dynamic>(content);

                    var results = new List<ModelWithCapabilities>();
                    if (modelsData?.data != null) {
                        foreach (var modelData in modelsData.data) {
                            var modelWithCaps = ParseLMStudioModelCapabilities(modelData);
                            if (modelWithCaps != null) {
                                results.Add(modelWithCaps);
                            }
                        }
                    }

                    _logger.LogInformation("获取到 {Count} 个LMStudio模型及其能力信息", results.Count);
                    return results;
                } else {
                    _logger.LogWarning("获取LMStudio模型失败: {StatusCode}", response.StatusCode);
                    // 回退到通用方式
                    var genericModels = await GetGenericOpenAICompatibleModels(channel);
                    return genericModels.Select(m => InferOpenAIModelCapabilities(m));
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "获取LMStudio模型能力时出错");
                return new List<ModelWithCapabilities>();
            }
        }

        /// <summary>
        /// 解析LMStudio模型能力信息
        /// LMStudio模型包含capabilities数组，如["tool_use", "vision", "chat", "text-completion"]
        /// </summary>
        private ModelWithCapabilities ParseLMStudioModelCapabilities(dynamic modelData) {
            try {
                string modelId = modelData.id?.ToString();
                if (string.IsNullOrEmpty(modelId)) {
                    return null;
                }

                var model = new ModelWithCapabilities { ModelName = modelId };

                // 基本能力
                model.SetCapability("streaming", true);
                model.SetCapability("chat", true);

                // 解析LMStudio的capabilities数组
                if (modelData.capabilities != null) {
                    foreach (var capability in modelData.capabilities) {
                        string capStr = capability.ToString().ToLower();
                        switch (capStr) {
                            case "tool_use":
                                model.SetCapability("function_calling", true);
                                model.SetCapability("tool_calls", true);
                                break;
                            case "vision":
                                model.SetCapability("vision", true);
                                model.SetCapability("multimodal", true);
                                model.SetCapability("image_content", true);
                                break;
                            case "embeddings":
                                model.SetCapability("embedding", true);
                                model.SetCapability("text_embedding", true);
                                break;
                            case "chat":
                                model.SetCapability("chat", true);
                                break;
                            case "text-completion":
                                model.SetCapability("text_generation", true);
                                break;
                        }
                    }
                }

                // 解析LMStudio扩展字段
                if (modelData.type != null) {
                    string modelType = modelData.type.ToString().ToLower();
                    if (modelType == "vlm") {
                        model.SetCapability("vision", true);
                        model.SetCapability("multimodal", true);
                    } else if (modelType == "embeddings") {
                        model.SetCapability("embedding", true);
                    }
                }

                if (modelData.max_context_length != null) {
                    model.SetCapability("input_token_limit", modelData.max_context_length.ToString());
                }

                if (modelData.arch != null) {
                    model.SetCapability("model_family", modelData.arch.ToString());
                }

                if (modelData.quantization != null) {
                    model.SetCapability("quantization", modelData.quantization.ToString());
                }

                model.SetCapability("provider", "LMStudio");

                return model;
            } catch (Exception ex) {
                string modelDataStr = modelData?.ToString() ?? "null";
                _logger.LogError(ex, "解析LMStudio模型能力时出错: {ModelData}", modelDataStr);
                return null;
            }
        }

        // --- Helper Methods (Defined locally again) ---

        public bool IsSameSender(Model.Data.Message message1, Model.Data.Message message2) {
            if (message1 == null || message2 == null) return false;
            bool msg1IsUser = message1.FromUserId != Env.BotId;
            bool msg2IsUser = message2.FromUserId != Env.BotId;
            return msg1IsUser == msg2IsUser;
        }

        private void AddMessageToHistory(List<ChatMessage> ChatHistory, long fromUserId, string content) {
            AddMessageToHistory(ChatHistory, fromUserId, content, null);
        }

        private void AddMessageToHistory(List<ChatMessage> ChatHistory, long fromUserId, string content, List<byte[]> images) {
            if (string.IsNullOrWhiteSpace(content) && ( images == null || images.Count == 0 )) return;
            if (!string.IsNullOrWhiteSpace(content)) {
                content = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\n{3,}", "\n\n");
            }

            if (fromUserId == Env.BotId) {
                ChatHistory.Add(new AssistantChatMessage(content?.Trim() ?? ""));
            } else {
                if (images != null && images.Count > 0) {
                    var parts = new List<ChatMessageContentPart>();
                    if (!string.IsNullOrWhiteSpace(content)) {
                        parts.Add(ChatMessageContentPart.CreateTextPart(content.Trim()));
                    }
                    foreach (var imageBytes in images) {
                        parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), "image/png"));
                    }
                    ChatHistory.Add(new UserChatMessage(parts));
                } else {
                    ChatHistory.Add(new UserChatMessage(content.Trim()));
                }
            }
        }

        public async Task<List<ChatMessage>> GetChatHistory(long ChatId, List<ChatMessage> ChatHistory, Model.Data.Message InputToken) {
            return await GetChatHistory(ChatId, ChatHistory, InputToken, false);
        }

        public async Task<List<ChatMessage>> GetChatHistory(long ChatId, List<ChatMessage> ChatHistory, Model.Data.Message InputToken, bool supportsVision) {
            var Messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == ChatId && m.DateTime > DateTime.UtcNow.AddHours(-1))
                            .OrderBy(m => m.DateTime)
                            .ToListAsync();
            if (Messages.Count < 10) {
                Messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == ChatId)
                            .OrderByDescending(m => m.DateTime)
                            .Take(10)
                            .OrderBy(m => m.DateTime)
                            .ToListAsync();
            }
            if (InputToken != null) {
                Messages.Add(InputToken);
            }
            _logger.LogInformation($"OpenAI GetChatHistory: Found {Messages.Count} messages for ChatId {ChatId}.");

            var str = new StringBuilder();
            Model.Data.Message previous = null;
            var userCache = new Dictionary<long, UserData>();
            var pendingImages = new List<byte[]>();

            foreach (var message in Messages) {
                if (previous == null && !ChatHistory.Any(ch => ch is UserChatMessage || ch is AssistantChatMessage) && message.FromUserId.Equals(Env.BotId)) {
                    previous = message;
                    continue;
                }

                if (previous != null && !IsSameSender(previous, message)) {
                    AddMessageToHistory(ChatHistory, previous.FromUserId, str.ToString(), supportsVision ? pendingImages : null);
                    str.Clear();
                    pendingImages.Clear();
                }

                str.Append($"[{message.DateTime.ToString("yyyy-MM-dd HH:mm:ss zzz")}]");
                if (message.FromUserId != 0) {
                    if (!userCache.TryGetValue(message.FromUserId, out var fromUser)) {
                        fromUser = await _dbContext.UserData.AsNoTracking().FirstOrDefaultAsync(u => u.Id == message.FromUserId);
                        if (fromUser != null) userCache[message.FromUserId] = fromUser;
                    }
                    str.Append(fromUser != null ? $"{fromUser.FirstName} {fromUser.LastName}".Trim() : $"User({message.FromUserId})");
                } else { str.Append("System/Unknown"); }

                if (message.ReplyToMessageId != 0) {
                    str.Append('（');
                    // Simplified reply info
                    str.Append($"Reply to msg {message.ReplyToMessageId}");
                    str.Append('）');
                }
                str.Append('：').Append(message.Content).Append("\n");

                // Add message extensions if any
                var extensions = await _messageExtensionService.GetByMessageDataIdAsync(message.Id);
                if (extensions != null && extensions.Any()) {
                    str.Append("[扩展信息：");
                    foreach (var ext in extensions) {
                        str.Append($"{ext.Name}={ext.Value}; ");
                    }
                    str.Append("]\n");
                }

                // 如果模型支持视觉，尝试加载消息关联的图片
                if (supportsVision && message.FromUserId != Env.BotId) {
                    var imageBytes = TryLoadMessagePhoto(message.GroupId, message.MessageId);
                    if (imageBytes != null) {
                        pendingImages.Add(imageBytes);
                    }
                }

                previous = message;
            }
            if (previous != null && str.Length > 0) {
                AddMessageToHistory(ChatHistory, previous.FromUserId, str.ToString(), supportsVision ? pendingImages : null);
            }
            return ChatHistory;
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

        // ConvertToolResultToString is now in McpToolHelper

        // --- Main Execution Logic ---
        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            var executionContext = new LlmExecutionContext();
            await foreach (var item in ExecAsync(message, ChatId, modelName, channel, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
                                                        LlmExecutionContext executionContext,
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(modelName)) modelName = Env.OpenAIModelName;

            if (string.IsNullOrWhiteSpace(modelName)) {
                _logger.LogError("{ServiceName}: Model name is not configured.", ServiceName);
                yield return $"Error: {ServiceName} model name is not configured.";
                yield break;
            }
            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway) || string.IsNullOrWhiteSpace(channel.ApiKey)) {
                _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured.", ServiceName);
                yield return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
                yield break;
            }

            // Try native tool calling first; fall back to XML prompt-based if it fails
            bool useNativeToolCalling = true;
            var nativeTools = McpToolHelper.GetNativeToolDefinitions();

            if (nativeTools == null || !nativeTools.Any()) {
                useNativeToolCalling = false;
            }

            if (useNativeToolCalling) {
                bool nativeFailed = false;
                var nativeEnumerator = ExecWithNativeToolCallingAsync(message, ChatId, modelName, channel, executionContext, nativeTools, cancellationToken);
                await using var enumerator = nativeEnumerator.GetAsyncEnumerator(cancellationToken);
                bool hasFirst = false;
                try {
                    hasFirst = await enumerator.MoveNextAsync();
                } catch (Exception ex) when (IsToolCallingNotSupportedError(ex)) {
                    _logger.LogInformation("{ServiceName}: Native tool calling not supported for model {Model}, falling back to XML prompt-based tool calling. Error: {Error}", ServiceName, modelName, ex.Message);
                    nativeFailed = true;
                }

                if (!nativeFailed) {
                    if (hasFirst) {
                        yield return enumerator.Current;
                        while (await enumerator.MoveNextAsync()) {
                            yield return enumerator.Current;
                        }
                    }
                    yield break;
                }
            }

            // Fallback: XML prompt-based tool calling
            await foreach (var item in ExecWithXmlToolCallingAsync(message, ChatId, modelName, channel, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        private static bool IsToolCallingNotSupportedError(Exception ex) {
            var message = ex.Message ?? "";
            // Check for ClientResultException with specific HTTP status codes
            if (ex is ClientResultException clientEx) {
                // 400 Bad Request with tool-related error message
                if (clientEx.Status == 400 && message.Contains("tool", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            // Common error patterns when a model/API doesn't support tool calling
            return message.Contains("tools", StringComparison.OrdinalIgnoreCase) &&
                   ( message.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("invalid", StringComparison.OrdinalIgnoreCase) ) ||
                   message.Contains("unrecognized request argument", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 检查模型是否支持视觉能力
        /// </summary>
        private async Task<bool> CheckVisionSupport(string modelName, int channelId) {
            try {
                var channelWithModel = await _dbContext.ChannelsWithModel
                    .Include(c => c.Capabilities)
                    .FirstOrDefaultAsync(c => c.ModelName == modelName && c.LLMChannelId == channelId && !c.IsDeleted);

                if (channelWithModel?.Capabilities != null) {
                    return channelWithModel.Capabilities.Any(c =>
                        c.CapabilityName == "vision" && c.CapabilityValue == "true");
                }

                return false;
            } catch (Exception ex) {
                _logger.LogDebug(ex, "检查模型视觉能力时出错: {ModelName}", modelName);
                return false;
            }
        }

        /// <summary>
        /// Execute LLM with native OpenAI tool calling API.
        /// </summary>
        private async IAsyncEnumerable<string> ExecWithNativeToolCallingAsync(
            Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
            LlmExecutionContext executionContext,
            List<ChatTool> nativeTools,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {

            // --- History and Prompt Setup (simplified - no XML tool instructions) ---
            string systemPrompt = McpToolHelper.FormatSystemPromptForNativeToolCalling(BotName, ChatId);
            List<ChatMessage> providerHistory = new List<ChatMessage>() { new SystemChatMessage(systemPrompt) };
            bool supportsVision = await CheckVisionSupport(modelName, channel.Id);
            providerHistory = await GetChatHistory(ChatId, providerHistory, message, supportsVision);

            using var client = _httpClientFactory.CreateClient();
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(client),
            };
            var chatClient = new ChatClient(model: modelName, credential: new(channel.ApiKey), clientOptions);

            var completionOptions = new ChatCompletionOptions();
            foreach (var tool in nativeTools) {
                completionOptions.Tools.Add(tool);
            }

            try {
                int maxToolCycles = Env.MaxToolCycles;
                var currentMessageContentBuilder = new StringBuilder();

                for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    var contentBuilder = new StringBuilder();
                    var reasoningContentBuilder = new StringBuilder();
                    var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();
                    ChatFinishReason? finishReason = null;

                    // --- Call LLM with tools ---
                    await foreach (var update in chatClient.CompleteChatStreamingAsync(providerHistory, completionOptions, cancellationToken).WithCancellation(cancellationToken)) {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                        // Accumulate text content
                        foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>()) {
                            if (updatePart?.Text != null) {
                                contentBuilder.Append(updatePart.Text);
                                currentMessageContentBuilder.Append(updatePart.Text);
                                if (currentMessageContentBuilder.ToString().Length > 10) {
                                    yield return currentMessageContentBuilder.ToString();
                                }
                            }
                        }

                        // Accumulate reasoning content for thinking mode models (e.g., Kimi-thinking-preview)
                        var reasoningUpdate = GetStreamingReasoningContent(update);
                        if (!string.IsNullOrEmpty(reasoningUpdate)) {
                            reasoningContentBuilder.Append(reasoningUpdate);
                        }

                        // Accumulate tool call updates
                        foreach (var toolCallUpdate in update.ToolCallUpdates ?? Enumerable.Empty<StreamingChatToolCallUpdate>()) {
                            int index = toolCallUpdate.Index;
                            if (!toolCallAccumulators.ContainsKey(index)) {
                                toolCallAccumulators[index] = new ToolCallAccumulator();
                            }

                            var acc = toolCallAccumulators[index];
                            if (toolCallUpdate.ToolCallId != null) acc.Id ??= toolCallUpdate.ToolCallId;
                            if (toolCallUpdate.FunctionName != null) acc.Name ??= toolCallUpdate.FunctionName;
                            if (toolCallUpdate.FunctionArgumentsUpdate != null) {
                                acc.Arguments.Append(toolCallUpdate.FunctionArgumentsUpdate);
                            }
                        }

                        if (update.FinishReason.HasValue) {
                            finishReason = update.FinishReason.Value;
                        }
                    }

                    string responseText = contentBuilder.ToString().Trim();
                    string reasoningContent = reasoningContentBuilder.ToString().Trim();

                    // Check if this is a tool call response
                    if (finishReason == ChatFinishReason.ToolCalls && toolCallAccumulators.Any()) {
                        // Build the assistant message with tool calls
                        var chatToolCalls = new List<ChatToolCall>();
                        foreach (var (index, acc) in toolCallAccumulators) {
                            if (acc.Id == null) {
                                _logger.LogWarning("{ServiceName}: Tool call at index {Index} has no ID, generating fallback.", ServiceName, index);
                            }
                            chatToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                                acc.Id ?? $"call_{Guid.NewGuid():N}",
                                acc.Name ?? "unknown",
                                BinaryData.FromString(acc.Arguments.ToString())));
                        }

                        var assistantMessage = new AssistantChatMessage(chatToolCalls);
                        if (!string.IsNullOrWhiteSpace(responseText)) {
                            assistantMessage = new AssistantChatMessage(chatToolCalls) { Content = { ChatMessageContentPart.CreateTextPart(responseText) } };
                        }
                        // Set reasoning content for thinking mode models (always call, even for empty)
                        SetAssistantReasoningContent(assistantMessage, reasoningContent ?? "");
                        providerHistory.Add(assistantMessage);

                        var toolIndicators = new StringBuilder();
                        foreach (var toolCall in chatToolCalls) {
                            var argsJson = toolCall.FunctionArguments?.ToString() ?? "{}";
                            var argsDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(argsJson)
                                ?? new Dictionary<string, string>();
                            toolIndicators.Append(McpToolHelper.FormatToolCallDisplay(toolCall.FunctionName, argsDict));
                        }
                        currentMessageContentBuilder.Append(toolIndicators.ToString());
                        yield return currentMessageContentBuilder.ToString();

                        // Execute each tool call
                        foreach (var toolCall in chatToolCalls) {
                            string toolName = toolCall.FunctionName;
                            _logger.LogInformation("{ServiceName}: Native tool call: {ToolName} with arguments: {Arguments}", ServiceName, toolName, toolCall.FunctionArguments?.ToString());

                            string toolResultString;
                            try {
                                // Parse arguments from JSON
                                var argsJson = toolCall.FunctionArguments?.ToString() ?? "{}";
                                var argsDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(argsJson)
                                    ?? new Dictionary<string, string>();

                                var toolContext = new ToolContext { ChatId = ChatId, UserId = message.FromUserId, MessageId = message.MessageId };
                                object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(toolName, argsDict, toolContext);
                                toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                                _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result length: {Length}", ServiceName, toolName, toolResultString.Length);
                            } catch (Exception ex) {
                                _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, toolName);
                                toolResultString = $"Error executing tool {toolName}: {ex.Message}";
                            }

                            // Add tool result to history using the proper ToolChatMessage
                            providerHistory.Add(new ToolChatMessage(toolCall.Id, toolResultString));
                        }

                        // Continue loop for next LLM call
                    } else {
                        // Not a tool call - regular text response
                        if (!string.IsNullOrWhiteSpace(responseText)) {
                            var assistantMsg = new AssistantChatMessage(responseText);
                            // Set reasoning content (always call, even for empty)
                            SetAssistantReasoningContent(assistantMsg, reasoningContent ?? "");
                            providerHistory.Add(assistantMsg);
                        }
                        yield break;
                    }
                }

                _logger.LogWarning("{ServiceName}: Max tool call cycles reached for chat {ChatId}. User confirmation needed.", ServiceName, ChatId);
                if (executionContext != null) {
                    executionContext.IterationLimitReached = true;
                    executionContext.SnapshotData = new LlmContinuationSnapshot {
                        ChatId = ChatId,
                        OriginalMessageId = message.MessageId,
                        UserId = message.FromUserId,
                        ModelName = modelName,
                        Provider = "OpenAI",
                        ChannelId = channel.Id,
                        LastAccumulatedContent = currentMessageContentBuilder.ToString(),
                        CyclesSoFar = maxToolCycles,
                        ProviderHistory = SerializeProviderHistory(providerHistory),
                    };
                }
            } finally {
                // No cleanup needed
            }
        }

        /// <summary>
        /// Execute LLM with XML prompt-based tool calling (fallback for models that don't support native tool calling).
        /// </summary>
        private async IAsyncEnumerable<string> ExecWithXmlToolCallingAsync(
            Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {

            // --- History and Prompt Setup ---
            string systemPrompt = McpToolHelper.FormatSystemPrompt(BotName, ChatId);
            List<ChatMessage> providerHistory = new List<ChatMessage>() { new SystemChatMessage(systemPrompt) };
            bool supportsVision = await CheckVisionSupport(modelName, channel.Id);
            providerHistory = await GetChatHistory(ChatId, providerHistory, message, supportsVision);

            using var client = _httpClientFactory.CreateClient();
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(client),
            };
            var chatClient = new ChatClient(model: modelName, credential: new(channel.ApiKey), clientOptions);

            try {
                int maxToolCycles = Env.MaxToolCycles;
                var currentMessageContentBuilder = new StringBuilder();

                for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    var llmResponseAccumulatorForToolParsing = new StringBuilder();

                    // --- Call LLM ---
                    await foreach (var update in chatClient.CompleteChatStreamingAsync(providerHistory, cancellationToken: cancellationToken).WithCancellation(cancellationToken)) {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                        foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>()) {
                            if (updatePart?.Text != null) {
                                currentMessageContentBuilder.Append(updatePart.Text);
                                llmResponseAccumulatorForToolParsing.Append(updatePart.Text);
                                if (currentMessageContentBuilder.ToString().Length > 10) {
                                    yield return currentMessageContentBuilder.ToString();
                                }
                            }
                        }
                    }
                    string llmFullResponseText = llmResponseAccumulatorForToolParsing.ToString().Trim();
                    _logger.LogDebug("{ServiceName} raw full response (Cycle {Cycle}): {Response}", ServiceName, cycle + 1, llmFullResponseText);

                    if (!string.IsNullOrWhiteSpace(llmFullResponseText)) {
                        providerHistory.Add(new AssistantChatMessage(llmFullResponseText));
                    } else if (cycle < maxToolCycles - 1) {
                        _logger.LogWarning("{ServiceName}: LLM returned empty response during tool cycle {Cycle}.", ServiceName, cycle + 1);
                    }

                    // --- Tool Handling (XML parsing) ---
                    if (McpToolHelper.TryParseToolCalls(llmFullResponseText, out var parsedToolCalls) && parsedToolCalls.Any()) {
                        var firstToolCall = parsedToolCalls[0];
                        string parsedToolName = firstToolCall.toolName;
                        Dictionary<string, string> toolArguments = firstToolCall.arguments;

                        _logger.LogInformation("{ServiceName}: LLM requested tool: {ToolName} with arguments: {Arguments}", ServiceName, parsedToolName, JsonConvert.SerializeObject(toolArguments));
                        if (parsedToolCalls.Count > 1) {
                            _logger.LogWarning("{ServiceName}: LLM returned multiple tool calls ({Count}). Only the first one ('{FirstToolName}') will be executed.", ServiceName, parsedToolCalls.Count, parsedToolName);
                        }

                        currentMessageContentBuilder.Append(McpToolHelper.FormatToolCallDisplay(parsedToolName, toolArguments));
                        yield return currentMessageContentBuilder.ToString();

                        string toolResultString;
                        bool isError = false;
                        try {
                            var toolContext = new ToolContext { ChatId = ChatId, UserId = message.FromUserId, MessageId = message.MessageId };
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments, toolContext);
                            toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result: {Result}", ServiceName, parsedToolName, toolResultString);
                        } catch (Exception ex) {
                            isError = true;
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName}.", ServiceName, parsedToolName);
                            toolResultString = $"Error executing tool {parsedToolName}: {ex.Message}.";
                        }

                        string feedbackPrefix = isError ? $"[Tool '{parsedToolName}' Execution Failed. Error: " : $"[Executed Tool '{parsedToolName}'. Result: ";
                        string feedback = $"{feedbackPrefix}{toolResultString}]";
                        providerHistory.Add(new UserChatMessage(feedback));
                        _logger.LogInformation("Added UserChatMessage to history for LLM: {Feedback}", feedback);
                    } else {
                        if (string.IsNullOrWhiteSpace(llmFullResponseText)) {
                            _logger.LogWarning("{ServiceName}: LLM returned empty final non-tool response for ChatId {ChatId}.", ServiceName, ChatId);
                        }
                        yield break;
                    }
                }

                _logger.LogWarning("{ServiceName}: Max tool call cycles reached for chat {ChatId}. User confirmation needed.", ServiceName, ChatId);
                if (executionContext != null) {
                    executionContext.IterationLimitReached = true;
                    executionContext.SnapshotData = new LlmContinuationSnapshot {
                        ChatId = ChatId,
                        OriginalMessageId = message.MessageId,
                        UserId = message.FromUserId,
                        ModelName = modelName,
                        Provider = "OpenAI",
                        ChannelId = channel.Id,
                        LastAccumulatedContent = currentMessageContentBuilder.ToString(),
                        CyclesSoFar = maxToolCycles,
                        ProviderHistory = SerializeProviderHistory(providerHistory),
                    };
                }
            } finally {
                // No cleanup needed for ToolContext
            }
        }

        /// <summary>
        /// Resume LLM execution from a saved snapshot, restoring the full provider history.
        /// </summary>
        public async IAsyncEnumerable<string> ResumeFromSnapshotAsync(LlmContinuationSnapshot snapshot, LLMChannel channel,
                                                                       LlmExecutionContext executionContext,
                                                                       [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (snapshot == null) {
                _logger.LogError("{ServiceName}: Cannot resume from null snapshot.", ServiceName);
                yield break;
            }
            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway) || string.IsNullOrWhiteSpace(channel.ApiKey)) {
                _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured for resume.", ServiceName);
                yield break;
            }

            var modelName = snapshot.ModelName;
            if (string.IsNullOrWhiteSpace(modelName)) modelName = Env.OpenAIModelName;

            _logger.LogInformation("{ServiceName}: Resuming from snapshot {SnapshotId} for ChatId {ChatId}, restoring {HistoryCount} history entries.",
                ServiceName, snapshot.SnapshotId, snapshot.ChatId, snapshot.ProviderHistory?.Count ?? 0);

            // Restore provider history from snapshot
            List<ChatMessage> providerHistory = DeserializeProviderHistory(snapshot.ProviderHistory);

            using var client = _httpClientFactory.CreateClient();
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(client),
            };
            var chatClient = new ChatClient(model: modelName, credential: new(channel.ApiKey), clientOptions);

            // Resume: only yield NEW content (the old content was already displayed to the user)
            // We keep a separate builder for full accumulated content (for snapshot) and one for new-only content (for yield)
            var fullContentBuilder = new StringBuilder(snapshot.LastAccumulatedContent ?? "");
            var newContentBuilder = new StringBuilder();

            try {
                int maxToolCycles = Env.MaxToolCycles;

                for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    var llmResponseAccumulatorForToolParsing = new StringBuilder();

                    await foreach (var update in chatClient.CompleteChatStreamingAsync(providerHistory, cancellationToken: cancellationToken).WithCancellation(cancellationToken)) {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                        foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>()) {
                            if (updatePart?.Text != null) {
                                fullContentBuilder.Append(updatePart.Text);
                                newContentBuilder.Append(updatePart.Text);
                                llmResponseAccumulatorForToolParsing.Append(updatePart.Text);
                                var newContent = newContentBuilder.ToString();
                                if (newContent.Length > 10) {
                                    yield return newContent;
                                }
                            }
                        }
                    }
                    string llmFullResponseText = llmResponseAccumulatorForToolParsing.ToString().Trim();
                    _logger.LogDebug("{ServiceName} raw full response (Resume Cycle {Cycle}): {Response}", ServiceName, cycle + 1, llmFullResponseText);

                    if (!string.IsNullOrWhiteSpace(llmFullResponseText)) {
                        providerHistory.Add(new AssistantChatMessage(llmFullResponseText));
                    }

                    if (McpToolHelper.TryParseToolCalls(llmFullResponseText, out var parsedToolCalls) && parsedToolCalls.Any()) {
                        var firstToolCall = parsedToolCalls[0];
                        string parsedToolName = firstToolCall.toolName;
                        Dictionary<string, string> toolArguments = firstToolCall.arguments;

                        _logger.LogInformation("{ServiceName}: LLM requested tool (resume): {ToolName}", ServiceName, parsedToolName);

                        var toolIndicator = McpToolHelper.FormatToolCallDisplay(parsedToolName, toolArguments);
                        newContentBuilder.Append(toolIndicator);
                        fullContentBuilder.Append(toolIndicator);
                        yield return newContentBuilder.ToString();

                        string toolResultString;
                        bool isError = false;
                        try {
                            var toolContext = new ToolContext { ChatId = snapshot.ChatId, UserId = snapshot.UserId, MessageId = snapshot.OriginalMessageId };
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, toolArguments, toolContext);
                            toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                        } catch (Exception ex) {
                            isError = true;
                            _logger.LogError(ex, "{ServiceName}: Error executing tool {ToolName} (resume).", ServiceName, parsedToolName);
                            toolResultString = $"Error executing tool {parsedToolName}: {ex.Message}.";
                        }

                        string feedbackPrefix = isError ? $"[Tool '{parsedToolName}' Execution Failed. Error: " : $"[Executed Tool '{parsedToolName}'. Result: ";
                        string feedback = $"{feedbackPrefix}{toolResultString}]";
                        providerHistory.Add(new UserChatMessage(feedback));
                    } else {
                        yield break;
                    }
                }

                _logger.LogWarning("{ServiceName}: Max tool call cycles reached again during resume for ChatId {ChatId}.", ServiceName, snapshot.ChatId);
                if (executionContext != null) {
                    executionContext.IterationLimitReached = true;
                    executionContext.SnapshotData = new LlmContinuationSnapshot {
                        ChatId = snapshot.ChatId,
                        OriginalMessageId = snapshot.OriginalMessageId,
                        UserId = snapshot.UserId,
                        ModelName = modelName,
                        Provider = "OpenAI",
                        ChannelId = channel.Id,
                        LastAccumulatedContent = fullContentBuilder.ToString(),
                        CyclesSoFar = snapshot.CyclesSoFar + maxToolCycles,
                        ProviderHistory = SerializeProviderHistory(providerHistory),
                    };
                }
            } finally {
                // No cleanup needed
            }
        }

        /// <summary>
        /// Serialize OpenAI ChatMessage list to portable format for snapshot persistence.
        /// </summary>
        public static List<SerializedChatMessage> SerializeProviderHistory(List<ChatMessage> history) {
            var result = new List<SerializedChatMessage>();
            foreach (var msg in history) {
                string role;
                string content = "";
                string? reasoningContent = null;

                if (msg is SystemChatMessage systemMsg) {
                    role = "system";
                    content = string.Join("", systemMsg.Content?.Select(p => p.Text) ?? Enumerable.Empty<string>());
                } else if (msg is AssistantChatMessage assistantMsg) {
                    role = "assistant";
                    content = string.Join("", assistantMsg.Content?.Select(p => p.Text) ?? Enumerable.Empty<string>());
                    // Try to get reasoning content from the assistant message
                    // OpenAI SDK stores reasoning content in a separate property
                    reasoningContent = GetAssistantReasoningContent(assistantMsg);
                } else if (msg is UserChatMessage userMsg) {
                    role = "user";
                    content = string.Join("", userMsg.Content?.Select(p => p.Text) ?? Enumerable.Empty<string>());
                } else {
                    role = "user";
                    content = msg.ToString();
                }

                result.Add(new SerializedChatMessage { Role = role, Content = content, ReasoningContent = reasoningContent });
            }
            return result;
        }

        /// <summary>
        /// Extract reasoning_content from AssistantChatMessage if available.
        /// For thinking mode models, the reasoning process is returned separately.
        /// </summary>
        private static string? GetAssistantReasoningContent(AssistantChatMessage assistantMsg) {
            // Try to access reasoning content - OpenAI Chat SDK may store it in various ways
            // The reasoning_content is typically available via reflection or specific properties
            try {
                // Check for Reasoning property via reflection
                var reasoningProp = assistantMsg.GetType().GetProperty("Reasoning");
                if (reasoningProp != null) {
                    var value = reasoningProp.GetValue(assistantMsg);
                    if (value is string reasoning && !string.IsNullOrEmpty(reasoning)) {
                        return reasoning;
                    }
                }
            } catch {
                // Reflection failed, return null
            }
            return null;
        }

        /// <summary>
        /// Extract reasoning_content from streaming update for thinking mode models.
        /// Uses Patch API first (OpenAI SDK), falls back to reflection for internal properties.
        /// </summary>
        private static string? GetStreamingReasoningContent(StreamingChatCompletionUpdate update) {
            // Primary: use Patch API to read reasoning_content from raw JSON response
#pragma warning disable SCME0001 // Patch is for evaluation, may be changed in future
            if (update.Patch.TryGetValue("$.choices[0].delta.reasoning_content"u8, out string? reasoningFromPatch)) {
                if (reasoningFromPatch != null) {
                    return reasoningFromPatch;
                }
            }
#pragma warning restore SCME0001

            // Fallback: reflection for SDK internal properties
            try {
                // Try ReasoningContentUpdate property (OpenAI SDK for thinking models)
                var reasoningProp = update.GetType().GetProperty("ReasoningContentUpdate");
                if (reasoningProp != null) {
                    var value = reasoningProp.GetValue(update);
                    if (value is string reasoning && !string.IsNullOrEmpty(reasoning)) {
                        return reasoning;
                    }
                }
                // Fallback: try Reasoning property
                var fallbackProp = update.GetType().GetProperty("Reasoning");
                if (fallbackProp != null) {
                    var value = fallbackProp.GetValue(update);
                    if (value is string fallback && !string.IsNullOrEmpty(fallback)) {
                        return fallback;
                    }
                }
            } catch {
                // Reflection failed
            }
            return null;
        }

        /// <summary>
        /// Deserialize portable format back to OpenAI ChatMessage list.
        /// </summary>
        public static List<ChatMessage> DeserializeProviderHistory(List<SerializedChatMessage> serialized) {
            var result = new List<ChatMessage>();
            if (serialized == null) return result;

            foreach (var msg in serialized) {
                switch (msg.Role?.ToLowerInvariant()) {
                    case "system":
                        result.Add(new SystemChatMessage(msg.Content ?? ""));
                        break;
                    case "assistant":
                        var assistantMsg = new AssistantChatMessage(msg.Content ?? "");
                        // Set reasoning content (always call, even for null)
                        SetAssistantReasoningContent(assistantMsg, msg.ReasoningContent ?? "");
                        result.Add(assistantMsg);
                        break;
                    case "user":
                    default:
                        result.Add(new UserChatMessage(msg.Content ?? ""));
                        break;
                }
            }
            return result;
        }

        /// <summary>
        /// Set reasoning_content on AssistantChatMessage for thinking mode models.
        /// Uses Patch.Set (OpenAI SDK v2.10.0+) with reflection fallback.
        /// </summary>
        private static void SetAssistantReasoningContent(AssistantChatMessage msg, string reasoningContent) {
            // Try Patch.Set first (writes directly to JSON output)
#pragma warning disable SCME0001 // Patch API is experimental but functional
            try {
                msg.Patch.Set("$.reasoning_content"u8, reasoningContent ?? "");
            } catch {
                // Patch.Set not available or failed, fall through to reflection
            }
#pragma warning restore SCME0001
            // Reflection fallback for older SDK versions
            try {
                var prop = msg.GetType().GetProperty("Reasoning");
                if (prop != null && prop.CanWrite) {
                    prop.SetValue(msg, reasoningContent);
                }
            } catch {
                // Reflection failed, ignore
            }
        }

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel) {


            using var httpClient = _httpClientFactory.CreateClient();

            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            var apikey = new ApiKeyCredential(channel.ApiKey);
            OpenAIClient client = new(apikey, clientOptions);

            try {
                var embeddingClient = client.GetEmbeddingClient(modelName);
                var response = await embeddingClient.GenerateEmbeddingsAsync(new[] { text });

                if (response?.Value != null && response.Value.Any()) {
                    var embedding = response.Value.First();
                    _logger.LogDebug("Embedding response type: {Type}", embedding.GetType().FullName);
                    _logger.LogDebug("Embedding response structure: {Response}", JsonConvert.SerializeObject(embedding, Formatting.Indented));

                    // Try reflection with all possible property names
                    var embeddingProp = embedding.GetType().GetProperty("Embedding")
                                      ?? embedding.GetType().GetProperty("EmbeddingVector")
                                      ?? embedding.GetType().GetProperty("Vector")
                                      ?? embedding.GetType().GetProperty("EmbeddingData")
                                      ?? embedding.GetType().GetProperty("Data");

                    if (embeddingProp != null) {
                        var embeddingValue = embeddingProp.GetValue(embedding);
                        if (embeddingValue is float[] floatArray) {
                            return floatArray;
                        } else if (embeddingValue is IEnumerable<float> floatEnumerable) {
                            return floatEnumerable.ToArray();
                        } else if (embeddingValue is IReadOnlyList<float> floatList) {
                            return floatList.ToArray();
                        }
                    }

                    // Last resort - try to find any float[] property
                    var floatArrayProps = embedding.GetType().GetProperties()
                        .Where(p => p.PropertyType == typeof(float[]) || p.PropertyType == typeof(IEnumerable<float>))
                        .ToList();

                    if (floatArrayProps.Any()) {
                        foreach (var prop in floatArrayProps) {
                            var value = prop.GetValue(embedding);
                            if (value is float[] floats) {
                                return floats;
                            } else if (value is IEnumerable<float> floatEnumerable) {
                                return floatEnumerable.ToArray();
                            }
                        }
                    }

                    _logger.LogError("Failed to extract embedding data. Available properties: {Props}",
                        string.Join(", ", embedding.GetType().GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
                }

                _logger.LogError("OpenAI Embeddings API returned null or empty response");
                throw new Exception("OpenAI Embeddings API returned null or empty response");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error calling OpenAI Embeddings API");
                throw;
            }
        }

        public async Task<(string, string)> SetModel(string ModelName, long ChatId) {
            var GroupSetting = await _dbContext.GroupSettings
                                .Where(s => s.GroupId == ChatId)
                                .FirstOrDefaultAsync();
            var CurrentModelName = GroupSetting?.LLMModelName;
            if (GroupSetting is null) {
                await _dbContext.AddAsync(new GroupSettings() { GroupId = ChatId, LLMModelName = ModelName });
            } else {
                GroupSetting.LLMModelName = ModelName;
            }
            await _dbContext.SaveChangesAsync();
            return (CurrentModelName ?? "Default", ModelName);
        }
        public async Task<string> GetModel(long ChatId) {
            var GroupSetting = await _dbContext.GroupSettings.AsNoTracking()
                                      .Where(s => s.GroupId == ChatId)
                                      .FirstOrDefaultAsync();
            var ModelName = GroupSetting?.LLMModelName;
            return ModelName;
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

            using var httpClient = _httpClientFactory.CreateClient();

            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(channel.Gateway),
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            var chatClient = new ChatClient(model: modelName, credential: new(channel.ApiKey), clientOptions);

            try {
                // 读取图像并转换为Base64
                using var fileStream = File.OpenRead(photoPath);
                var tg_img = SKBitmap.Decode(fileStream);
                var tg_img_data = tg_img.Encode(SKEncodedImageFormat.Png, 99);
                var tg_img_arr = tg_img_data.ToArray();
                var base64Image = Convert.ToBase64String(tg_img_arr);

                var messages = new List<ChatMessage> {
                    new UserChatMessage(new List<ChatMessageContentPart>() {
                        ChatMessageContentPart.CreateTextPart(prompt),
                        ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(tg_img_arr), "image/png"),

                    }),
                };

                var responseBuilder = new StringBuilder();
                await foreach (var update in chatClient.CompleteChatStreamingAsync(messages)) {
                    foreach (ChatMessageContentPart updatePart in update.ContentUpdate ?? Enumerable.Empty<ChatMessageContentPart>()) {
                        if (updatePart?.Text != null) responseBuilder.Append(updatePart.Text);
                    }
                }
                return responseBuilder.ToString();
            } catch (Exception ex) {
                _logger.LogError(ex, "Error analyzing image with OpenAI");
                return $"Error analyzing image: {ex.Message}";
            }
        }
    }
}

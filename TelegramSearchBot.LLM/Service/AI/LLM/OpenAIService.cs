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
using System.Text.RegularExpressions;
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
    public class OpenAIService : IService, ILlmProvider {
        public string ServiceName => "OpenAIService";

        /// <summary>
        /// Mutable accumulator for streaming tool call updates.
        /// </summary>
        private class ToolCallAccumulator {
            public string Id { get; set; }
            public string Name { get; set; }
            public StringBuilder Arguments { get; } = new StringBuilder();
        }

        internal static string NormalizeToolCallId(string toolCallId) {
            return string.IsNullOrWhiteSpace(toolCallId)
                ? $"call_{Guid.NewGuid():N}"
                : toolCallId.Trim();
        }

        internal static string NormalizeToolCallName(string toolCallName) {
            return string.IsNullOrWhiteSpace(toolCallName)
                ? "unknown"
                : toolCallName.Trim();
        }

        internal static string NormalizeToolCallArguments(string argumentsJson) {
            return string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
        }

        internal static Dictionary<string, string> DeserializeToolArgumentsForDisplay(string argumentsJson) {
            try {
                var normalized = NormalizeToolCallArguments(argumentsJson);
                var values = JsonConvert.DeserializeObject<Dictionary<string, object>>(normalized);
                return values?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.ToString() ?? string.Empty)
                    ?? new Dictionary<string, string>();
            } catch {
                return new Dictionary<string, string>();
            }
        }

        internal static bool IsMiniMaxCompatibleEndpoint(LLMChannel channel, string modelName) {
            if (channel?.Provider == LLMProvider.MiniMax) {
                return true;
            }

            var gateway = channel?.Gateway ?? string.Empty;
            var model = modelName ?? string.Empty;
            return gateway.Contains("minimax", StringComparison.OrdinalIgnoreCase) ||
                   model.Contains("minimax", StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizeOpenAIEndpoint(LLMChannel channel, string endpoint = null) {
            // endpoint 为 binding 解析后的地址（无 binding 时回退 channel.Gateway，blueprint §六.1）
            var gateway = endpoint ?? channel?.Gateway ?? string.Empty;
            if (channel?.Provider != LLMProvider.MiniMax) {
                return gateway;
            }

            gateway = gateway.TrimEnd('/');
            return !string.IsNullOrEmpty(gateway) &&
                   !gateway.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{gateway}/v1"
                : gateway;
        }

        private static string SanitizeAndTruncateArguments(string arguments, int maxChars = 2048) {
            if (string.IsNullOrWhiteSpace(arguments)) {
                return string.Empty;
            }

            var sanitized = arguments;
            var sensitiveKeys = new[] { "api_key", "apikey", "apiKey", "token", "password", "secret", "authorization" };
            foreach (var key in sensitiveKeys) {
                sanitized = Regex.Replace(
                    sanitized,
                    $"(\"{Regex.Escape(key)}\"\\s*:\\s*\")[^\"]*(\")",
                    "$1***$2",
                    RegexOptions.IgnoreCase);
            }

            sanitized = sanitized.Replace("\r", "\\r").Replace("\n", "\\n");
            return sanitized.Length <= maxChars
                ? sanitized
                : sanitized.Substring(0, maxChars) + $"...<truncated {sanitized.Length - maxChars} chars>";
        }

        private readonly ILogger<OpenAIService> _logger;
        private readonly DataDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMessageExtensionService _messageExtensionService;
        private readonly IBotIdentityProvider _botIdentityProvider;
        private readonly IGroupLlmSettingsService _groupLlmSettingsService;
        private readonly LlmVisibilityService _llmVisibilityService;
        private readonly PromptCachingSettingsService _promptCachingSettingsService;
        private readonly LlmChatRunner _chatRunner;
        private string _fallbackBotName = string.Empty;

        public OpenAIService(
            DataDbContext context,
            ILogger<OpenAIService> logger,
            IMessageExtensionService messageExtensionService,
            IHttpClientFactory httpClientFactory)
            : this(context, logger, messageExtensionService, httpClientFactory, null, null, null, null) {
        }

        public OpenAIService(
            DataDbContext context,
            ILogger<OpenAIService> logger,
            IMessageExtensionService messageExtensionService,
            IHttpClientFactory httpClientFactory,
            IBotIdentityProvider botIdentityProvider,
            IGroupLlmSettingsService groupLlmSettingsService,
            LlmVisibilityService llmVisibilityService = null,
            PromptCachingSettingsService promptCachingSettingsService = null,
            LlmChatRunner chatRunner = null) {
            _logger = logger;
            _dbContext = context;
            _messageExtensionService = messageExtensionService;
            _httpClientFactory = httpClientFactory;
            _botIdentityProvider = botIdentityProvider;
            _groupLlmSettingsService = groupLlmSettingsService;
            _llmVisibilityService = llmVisibilityService;
            _promptCachingSettingsService = promptCachingSettingsService;
            _chatRunner = chatRunner;
            _logger.LogInformation("OpenAIService instance created. McpToolHelper should be initialized at application startup.");
        }

        public string BotName {
            get => GetBotNameAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            set {
                if (_botIdentityProvider != null) {
                    _botIdentityProvider.SetIdentity(Env.BotId, value);
                } else {
                    _fallbackBotName = value ?? string.Empty;
                }
            }
        }

        private async Task<string> GetBotNameAsync() {
            if (_botIdentityProvider == null) {
                return _fallbackBotName;
            }

            var identity = await _botIdentityProvider.GetIdentityAsync();
            return identity.UserName ?? string.Empty;
        }

        private async Task<bool> IsPromptCachingEnabledAsync() {
            return _promptCachingSettingsService == null || await _promptCachingSettingsService.IsEnabledAsync();
        }

        private static List<ChatMessage> GetStablePrefixMessages(List<ChatMessage> providerHistory, bool excludeDynamicTail) {
            if (!excludeDynamicTail || providerHistory.Count <= 1) {
                return providerHistory.ToList();
            }

            return providerHistory.Take(providerHistory.Count - 1).ToList();
        }

        /// <summary>Compact, deterministic history serialization for prompt-cache key hashing.</summary>
        internal static List<SerializedChatMessage> SerializeProviderHistory(List<ChatMessage> history) {
            var result = new List<SerializedChatMessage>();
            foreach (var msg in history) {
                string role;
                string content;
                if (msg is SystemChatMessage systemMsg) {
                    role = "system";
                    content = string.Join("", systemMsg.Content?.Select(p => p.Text) ?? Enumerable.Empty<string>());
                } else if (msg is AssistantChatMessage assistantMsg) {
                    role = "assistant";
                    content = string.Join("", assistantMsg.Content?.Select(p => p.Text) ?? Enumerable.Empty<string>());
                } else if (msg is UserChatMessage userMsg) {
                    role = "user";
                    content = string.Join("", userMsg.Content?.Select(p => p.Text) ?? Enumerable.Empty<string>());
                } else {
                    role = "user";
                    content = msg.ToString() ?? string.Empty;
                }
                result.Add(new SerializedChatMessage { Role = role, Content = content });
            }
            return result;
        }

        internal static (string toolDefinitionHash, string stablePrefixHash, string promptCacheKey) BuildPromptCachingContext(
            string providerName,
            string modelName,
            string mode,
            List<ChatMessage> providerHistory,
            bool excludeDynamicTail) {
            var toolDefinitionHash = PromptCachingHelper.ComputeToolDefinitionHash();
            var stablePrefixHash = PromptCachingHelper.ComputeStablePrefixHash(new {
                Mode = mode,
                StableHistory = SerializeProviderHistory(GetStablePrefixMessages(providerHistory, excludeDynamicTail)),
            });
            var promptCacheKey = PromptCachingHelper.BuildOpenAiPromptCacheKey(providerName, modelName, toolDefinitionHash, stablePrefixHash);
            return (toolDefinitionHash, stablePrefixHash, promptCacheKey);
        }

        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            if (channel.Provider.Equals(LLMProvider.Ollama)) {
                return new List<string>();
            }

            if (channel.Provider == LLMProvider.MiniMax) {
                var models = await GetGenericOpenAICompatibleModels(channel);
                return models.Any() ? models : _miniMaxModels;
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
                OpencodeSessionHeaders.Apply(httpClient, channel.Gateway);

                // --- Client Setup ---
                var clientOptions = new OpenAIClientOptions {
                    Endpoint = new Uri(NormalizeOpenAIEndpoint(channel)),
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

        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel, LLMApiBinding binding) {
            if (channel == null) return new List<string>();
            if (binding == null) return await GetAllModels(channel);

            // binding 路由：确定性 endpoint，不做品牌/URL 猜测（blueprint §六.7）
            var genericModels = await GetGenericOpenAICompatibleModels(channel, binding);
            if (genericModels.Any()) {
                return genericModels;
            }

            try {
                var handler = new HttpClientHandler {
                    Proxy = WebRequest.DefaultWebProxy,
                    UseProxy = true
                };

                using var httpClient = new HttpClient(handler);

                // --- Client Setup ---
                var clientOptions = new OpenAIClientOptions {
                    Endpoint = new Uri(LlmBindingSupport.ResolveEndpoint(channel, binding)),
                    Transport = new HttpClientPipelineTransport(httpClient),
                };

                var apikey = new ApiKeyCredential(LlmBindingSupport.ResolveApiKey(channel, binding));

                OpenAIClient client = new(apikey, clientOptions);
                var model = client.GetOpenAIModelClient();
                var models = await model.GetModelsAsync();
                return from s in models.Value
                       select s.Id;
            } catch (Exception ex) {
                _logger.LogError(ex, "使用 OpenAI SDK 获取模型列表失败 (Gateway: {Gateway})", LlmBindingSupport.ResolveEndpoint(channel, binding));
                return new List<string>();
            }
        }

        public async Task<bool> IsHealthyAsync(LLMChannel channel, LLMApiBinding binding) {
            var models = await GetAllModels(channel, binding);
            return models.Any();
        }

        /// <summary>
        /// 使用通用 HTTP GET 方式获取 OpenAI 兼容 API 的模型列表，兼容 MiniMax、DeepSeek 等提供商
        /// </summary>
        private async Task<IEnumerable<string>> GetGenericOpenAICompatibleModels(LLMChannel channel, LLMApiBinding? binding = null) {
            try {
                var httpClient = _httpClientFactory.CreateClient();
                var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
                if (!string.IsNullOrEmpty(apiKey)) {
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                }
                OpencodeSessionHeaders.Apply(httpClient, channel, binding);

                // 构建模型列表 URL，确保路径正确
                var gatewayBase = NormalizeOpenAIEndpoint(channel, LlmBindingSupport.ResolveEndpoint(channel, binding)).TrimEnd('/');
                var modelsUrl = gatewayBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                    ? $"{gatewayBase}/models"
                    : $"{gatewayBase}/v1/models";

                var response = await httpClient.GetAsync(modelsUrl);
                if (!response.IsSuccessStatusCode && channel.Provider != LLMProvider.MiniMax) {
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
        /// MiniMax OpenAI-compatible text model fallback snapshot.
        /// </summary>
        private static readonly string[] _miniMaxModels = {
            "MiniMax-M3",
            "MiniMax-M2.7",
            "MiniMax-M2.7-highspeed",
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

            if (channel.Provider == LLMProvider.MiniMax) {
                var models = await GetAllModels(channel);
                return models.Select(InferOpenAIModelCapabilities);
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
                OpencodeSessionHeaders.Apply(httpClient, channel.Gateway);

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
                    Endpoint = new Uri(NormalizeOpenAIEndpoint(channel)),
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
            // 图片生成模型
            else if (ModelWithCapabilities.IsKnownImageGenerationModelName(modelName)) {
                model.SetCapability("image_generation", true);
                model.SetCapability("text_to_image", true);
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
            await foreach (var item in ExecAsync(message, ChatId, modelName, channel, null, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
                                                        LLMApiBinding binding,
                                                        LlmExecutionContext executionContext,
                                                        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(modelName)) modelName = Env.OpenAIModelName;

            if (string.IsNullOrWhiteSpace(modelName)) {
                _logger.LogError("{ServiceName}: Model name is not configured.", ServiceName);
                yield return $"Error: {ServiceName} model name is not configured.";
                yield break;
            }
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured.", ServiceName);
                yield return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
                yield break;
            }

            var rows = await LlmHistoryQueryService.LoadAsync(_dbContext, _llmVisibilityService, ChatId, message, cancellationToken);
            var supportsVision = await CheckVisionSupport(modelName, channel.Id);

            // Try native tool calling first; fall back to XML prompt-based if it fails
            var nativeTools = McpToolHelper.GetNativeToolDefinitions();
            var useNativeToolCalling = nativeTools is { Count: > 0 };

            if (useNativeToolCalling) {
                bool nativeFailed = false;
                var nativeEnumerator = ExecWithNativeToolCallingAsync(rows, supportsVision, message, ChatId, modelName, channel, binding, executionContext, nativeTools, cancellationToken);
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
            await foreach (var item in ExecWithXmlToolCallingAsync(rows, supportsVision, message, ChatId, modelName, channel, binding, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> ExecWithHistoryAsync(
            IReadOnlyList<AgentHistoryMessage> history,
            Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
            LLMApiBinding binding, LlmExecutionContext executionContext,
            bool supportsVision,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            // Glue (client construction, native/XML fallback dispatch) lives in LlmChatRunner.
            await foreach (var item in _chatRunner.RunAsync(history, message, ChatId, modelName, channel, binding, executionContext, supportsVision, cancellationToken)) {
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

        private async IAsyncEnumerable<string> ExecWithNativeToolCallingAsync(
            IReadOnlyList<AgentHistoryMessage> rows,
            bool supportsVision,
            Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
            LLMApiBinding binding,
            LlmExecutionContext executionContext,
            List<ChatTool> nativeTools,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();

            var botName = await GetBotNameAsync();
            string systemPrompt = McpToolHelper.FormatSystemPromptForNativeToolCalling(botName, ChatId);
            var promptCachingEnabled = channel.Provider == LLMProvider.OpenAI && await IsPromptCachingEnabledAsync();
            var includeEmptyReasoningContent = binding == null && ShouldIncludeEmptyReasoningContent(channel, modelName);

            var clientParts = BuildClientParts(channel, binding, ChatId);
            var transport = new Transports.OpenAiChatTransport(_logger, clientParts.Transport, clientParts.Credential, clientParts.Options, modelName, channel,
                nativeTools: true, promptCachingEnabled, includeEmptyReasoningContent);
            var history = LlmHistoryProjector.Project(rows, supportsVision, _logger);

            var meta = new LlmToolLoopMeta {
                ChatId = ChatId,
                OriginalMessageId = message.MessageId,
                UserId = message.FromUserId,
                ModelName = modelName,
                Provider = "OpenAI",
                ChannelId = channel.Id
            };
            var toolContext = new ToolContext { ChatId = ChatId, UserId = message.FromUserId, MessageId = message.MessageId };
            var run = new LlmAgentRunRequest {
                Transport = transport,
                SystemPrompt = systemPrompt,
                History = history,
                Tools = McpToolHelper.GetLlmToolSpecs(),
                Config = BuildConfig(modelName, channel, binding, supportsVision, promptCachingEnabled, includeEmptyReasoningContent),
                ToolContext = toolContext,
                Meta = meta,
                ExecutionContext = executionContext
            };
            await foreach (var item in LlmToolLoop.RunAsync(run, cancellationToken)) {
                yield return item;
            }
        }

        private async IAsyncEnumerable<string> ExecWithXmlToolCallingAsync(
            IReadOnlyList<AgentHistoryMessage> rows,
            bool supportsVision,
            Model.Data.Message message, long ChatId, string modelName, LLMChannel channel,
            LLMApiBinding binding,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();

            var botName = await GetBotNameAsync();
            string systemPrompt = McpToolHelper.FormatSystemPrompt(botName, ChatId);
            var promptCachingEnabled = channel.Provider == LLMProvider.OpenAI && await IsPromptCachingEnabledAsync();
            var includeEmptyReasoningContent = binding == null && ShouldIncludeEmptyReasoningContent(channel, modelName);

            var clientParts = BuildClientParts(channel, binding, ChatId);
            var transport = new Transports.OpenAiChatTransport(_logger, clientParts.Transport, clientParts.Credential, clientParts.Options, modelName, channel,
                nativeTools: false, promptCachingEnabled, includeEmptyReasoningContent);
            var history = LlmHistoryProjector.Project(rows, supportsVision, _logger);

            var meta = new LlmToolLoopMeta {
                ChatId = ChatId,
                OriginalMessageId = message.MessageId,
                UserId = message.FromUserId,
                ModelName = modelName,
                Provider = "OpenAI",
                ChannelId = channel.Id
            };
            var toolContext = new ToolContext { ChatId = ChatId, UserId = message.FromUserId, MessageId = message.MessageId };
            var run = new LlmAgentRunRequest {
                Transport = transport,
                SystemPrompt = systemPrompt,
                History = history,
                Tools = null,
                Config = BuildConfig(modelName, channel, binding, supportsVision, promptCachingEnabled, includeEmptyReasoningContent),
                ToolContext = toolContext,
                Meta = meta,
                ExecutionContext = executionContext
            };
            await foreach (var item in LlmToolLoop.RunAsync(run, cancellationToken)) {
                yield return item;
            }
        }

        private LlmTransportConfig BuildConfig(string modelName, LLMChannel channel, LLMApiBinding binding,
            bool supportsVision, bool promptCachingEnabled, bool includeEmptyReasoningContent) {
            return new LlmTransportConfig {
                ModelName = modelName,
                Endpoint = NormalizeOpenAIEndpoint(channel, LlmBindingSupport.ResolveEndpoint(channel, binding)),
                ApiKey = LlmBindingSupport.ResolveApiKey(channel, binding),
                Provider = channel.Provider,
                Binding = binding,
                Channel = channel,
                SupportsVision = supportsVision,
                PromptCachingEnabled = promptCachingEnabled,
                IncludeEmptyReasoningContent = includeEmptyReasoningContent
            };
        }

        private (HttpClientPipelineTransport Transport, ApiKeyCredential Credential, OpenAIClientOptions Options) BuildClientParts(LLMChannel channel, LLMApiBinding binding, long chatId) {
            // ponytail: HttpClient must outlive this method (transport persists per run); factory-managed, not disposed here.
            var httpClient = _httpClientFactory.CreateClient();
            OpencodeSessionHeaders.Apply(httpClient, channel, binding, $"tsb-{chatId}");
            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(NormalizeOpenAIEndpoint(channel, LlmBindingSupport.ResolveEndpoint(channel, binding))),
                Transport = new HttpClientPipelineTransport(httpClient),
            };
            return (new HttpClientPipelineTransport(httpClient), new ApiKeyCredential(LlmBindingSupport.ResolveApiKey(channel, binding)), clientOptions);
        }

        /// <summary>
        /// Resume LLM execution from a saved v2 (normalized) snapshot.
        /// </summary>
        public async IAsyncEnumerable<string> ResumeFromSnapshotAsync(LlmContinuationSnapshot snapshot, LLMChannel channel,
                                                                       LlmExecutionContext executionContext,
                                                                       [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var item in ResumeFromSnapshotAsync(snapshot, channel, null, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        public async IAsyncEnumerable<string> ResumeFromSnapshotAsync(LlmContinuationSnapshot snapshot, LLMChannel channel,
                                                                       LLMApiBinding binding,
                                                                       LlmExecutionContext executionContext,
                                                                       [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();
            if (snapshot == null) {
                _logger.LogError("{ServiceName}: Cannot resume from null snapshot.", ServiceName);
                yield break;
            }
            if (snapshot.NormalizedHistory is not { Count: > 0 } savedHistory) {
                _logger.LogError("{ServiceName}: Snapshot {SnapshotId} has no v2 normalized history (legacy v1 snapshots expire via TTL).", ServiceName, snapshot.SnapshotId);
                yield break;
            }
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured for resume.", ServiceName);
                yield break;
            }

            var modelName = snapshot.ModelName;
            if (string.IsNullOrWhiteSpace(modelName)) modelName = Env.OpenAIModelName;

            _logger.LogInformation("{ServiceName}: Resuming from snapshot {SnapshotId} for ChatId {ChatId}, restoring {HistoryCount} history entries.",
                ServiceName, snapshot.SnapshotId, snapshot.ChatId, savedHistory.Count);

            // Peel the stored system prompt off the normalized history.
            string systemPrompt;
            if (savedHistory[0].Role == LlmRole.System) {
                systemPrompt = savedHistory[0].Text ?? string.Empty;
                savedHistory = savedHistory.Skip(1).ToList();
            } else {
                var botName = await GetBotNameAsync();
                systemPrompt = McpToolHelper.FormatSystemPromptForNativeToolCalling(botName, snapshot.ChatId);
            }

            var promptCachingEnabled = channel.Provider == LLMProvider.OpenAI && await IsPromptCachingEnabledAsync();
            var includeEmptyReasoningContent = binding == null && ShouldIncludeEmptyReasoningContent(channel, modelName);
            var clientParts = BuildClientParts(channel, binding, snapshot.ChatId);
            var transport = new Transports.OpenAiChatTransport(_logger, clientParts.Transport, clientParts.Credential, clientParts.Options, modelName, channel,
                nativeTools: false, promptCachingEnabled, includeEmptyReasoningContent);

            var meta = new LlmToolLoopMeta {
                ChatId = snapshot.ChatId,
                OriginalMessageId = snapshot.OriginalMessageId,
                UserId = snapshot.UserId,
                ModelName = modelName,
                Provider = "OpenAI",
                ChannelId = channel.Id,
                BaseCycles = snapshot.CyclesSoFar,
                InitialContent = snapshot.LastAccumulatedContent ?? string.Empty
            };
            var toolContext = new ToolContext { ChatId = snapshot.ChatId, UserId = snapshot.UserId, MessageId = snapshot.OriginalMessageId };
            var run = new LlmAgentRunRequest {
                Transport = transport,
                SystemPrompt = systemPrompt,
                History = savedHistory,
                Tools = null,
                Config = BuildConfig(modelName, channel, binding, false, promptCachingEnabled, includeEmptyReasoningContent),
                ToolContext = toolContext,
                Meta = meta,
                ExecutionContext = executionContext
            };
            await foreach (var item in LlmToolLoop.RunAsync(run, cancellationToken)) {
                yield return item;
            }
        }

        /// <summary>
        /// Stateless transport adapter for the OpenAI Chat Completions API: converts the
        /// normalized history into SDK messages per turn and streams one assistant turn.
        /// </summary>
        internal static string? GetAssistantReasoningContent(AssistantChatMessage assistantMsg) {
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

                #pragma warning disable SCME0001 // Patch is for evaluation, may be changed in future
                var patchProp = assistantMsg.GetType().GetProperty("Patch");
                if (patchProp?.GetValue(assistantMsg) is JsonPatch patch &&
                    patch.TryGetValue("$.reasoning_content"u8, out string? reasoningFromPatch)) {
                    return reasoningFromPatch;
                }
                #pragma warning restore SCME0001
            } catch {
                // Reflection failed, return null
            }
            return null;
        }

        /// <summary>
        /// Extract reasoning_content from streaming update for thinking mode models.
        /// Uses Patch API first (OpenAI SDK), falls back to reflection for internal properties.
        /// </summary>
        internal static string? GetStreamingReasoningContent(StreamingChatCompletionUpdate update) {
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
        public static List<ChatMessage> DeserializeProviderHistory(List<SerializedChatMessage> serialized, bool includeEmptyReasoningContent = false) {
            var result = new List<ChatMessage>();
            if (serialized == null) return result;

            foreach (var msg in serialized) {
                switch (msg.Role?.ToLowerInvariant()) {
                    case "system":
                        result.Add(new SystemChatMessage(msg.Content ?? ""));
                        break;
                    case "assistant":
                        var assistantMsg = new AssistantChatMessage(msg.Content ?? "");
                        SetAssistantReasoningContent(assistantMsg, msg.ReasoningContent, includeEmptyReasoningContent);
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
        internal static bool ShouldIncludeEmptyReasoningContent(LLMChannel channel, string modelName) {
            var gateway = channel?.Gateway ?? string.Empty;
            var model = modelName ?? string.Empty;
            return gateway.Contains("deepseek", StringComparison.OrdinalIgnoreCase) ||
                   model.Contains("deepseek", StringComparison.OrdinalIgnoreCase);
        }

        internal static void SetAssistantReasoningContent(AssistantChatMessage msg, string reasoningContent, bool includeEmptyReasoningContent = false) {
            if (reasoningContent is null || (reasoningContent.Length == 0 && !includeEmptyReasoningContent)) {
                return;
            }

            // Try Patch.Set first (writes directly to JSON output)
#pragma warning disable SCME0001 // Patch API is experimental but functional
            try {
                var encodedReasoningContent = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(reasoningContent);
                msg.Patch.Set("$.reasoning_content"u8, encodedReasoningContent.AsSpan());
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
            return await GenerateEmbeddingsAsync(text, modelName, channel, null);
        }

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel, LLMApiBinding binding) {
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel, Gateway, or ApiKey is not configured.", ServiceName);
                throw new InvalidOperationException($"Error: {ServiceName} channel/gateway/apikey is not configured.");
            }

            using var httpClient = _httpClientFactory.CreateClient();
            OpencodeSessionHeaders.Apply(httpClient, channel, binding);

            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(NormalizeOpenAIEndpoint(channel, endpoint)),
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            var apikey = new ApiKeyCredential(apiKey);
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
            if (_groupLlmSettingsService != null) {
                var (previous, current) = await _groupLlmSettingsService.SetModelAsync(ChatId, ModelName);
                return (previous, current);
            }

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
            if (_groupLlmSettingsService != null) {
                return await _groupLlmSettingsService.GetModelAsync(ChatId);
            }

            var GroupSetting = await _dbContext.GroupSettings.AsNoTracking()
                                      .Where(s => s.GroupId == ChatId)
                                      .FirstOrDefaultAsync();
            var ModelName = GroupSetting?.LLMModelName;
            return ModelName;
        }

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, string prompt = null) {
            return await AnalyzeImageAsync(photoPath, modelName, channel, null, prompt);
        }

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, LLMApiBinding binding, string prompt = null) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                modelName = "gpt-4-vision-preview";
            }

            prompt = string.IsNullOrWhiteSpace(prompt) ? GeneralLLMService.DefaultAltPhotoPrompt : prompt;

            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel, Gateway or ApiKey is not configured.", ServiceName);
                return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
            }

            using var httpClient = _httpClientFactory.CreateClient();
            OpencodeSessionHeaders.Apply(httpClient, channel, binding);

            var clientOptions = new OpenAIClientOptions {
                Endpoint = new Uri(NormalizeOpenAIEndpoint(channel, LlmBindingSupport.ResolveEndpoint(channel, binding))),
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            var chatClient = new ChatClient(model: modelName, credential: new ApiKeyCredential(LlmBindingSupport.ResolveApiKey(channel, binding)), clientOptions);

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

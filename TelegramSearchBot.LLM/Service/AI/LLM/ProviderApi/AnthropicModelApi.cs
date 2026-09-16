using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Messages;
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
// Alias to resolve ambiguity between TelegramSearchBot.Model.Data.Message and Anthropic.Models.Messages.Message
using DataMessage = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Service.AI.LLM {
    [Injectable(ServiceLifetime.Transient)]
    public class AnthropicModelApi : ILlmModelCatalog, ILlmEmbeddings, ILlmVision {
        private const string ServiceName = "AnthropicModelApi";

        private readonly ILogger<AnthropicModelApi> _logger;
        private readonly DataDbContext _dbContext;
        private readonly IMessageExtensionService _messageExtensionService;
        private readonly IHttpClientFactory _httpClientFactory;


        private static readonly string[] _anthropicModels = {
            "claude-sonnet-4-20250514",
            "claude-opus-4-20250514",
            "claude-3-5-sonnet-20241022",
            "claude-3-5-haiku-20241022",
            "claude-3-opus-20240229",
            "claude-3-sonnet-20240229",
            "claude-3-haiku-20240307"
        };

        public AnthropicModelApi(
            DataDbContext context,
            ILogger<AnthropicModelApi> logger,
            IMessageExtensionService messageExtensionService,
            IHttpClientFactory httpClientFactory) {
            _logger = logger;
            _dbContext = context;
            _messageExtensionService = messageExtensionService;
            _httpClientFactory = httpClientFactory;
            _logger.LogInformation("AnthropicModelApi instance created.");
        }


        private AnthropicClient CreateClient(LLMChannel channel, LLMApiBinding? binding = null) {
            var options = new Anthropic.Core.ClientOptions {
                ApiKey = LlmBindingSupport.ResolveApiKey(channel, binding),
            };
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            if (!string.IsNullOrWhiteSpace(endpoint)) {
                // Binding URL 已含 /v1（如 https://opencode.ai/zen/v1），SDK 会再追加 /v1/messages；
                // 剥离尾部 /v1 使 SDK 追加后命中精确 binding 路径。legacy channel.Gateway 保持字节一致。
                var trimmed = endpoint.TrimEnd('/');
                if (binding != null && trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
                    trimmed = trimmed.Substring(0, trimmed.Length - 3);
                }
                options.BaseUrl = trimmed;
            }
            return new AnthropicClient(options);
        }


        internal static CacheControlEphemeral CreateCacheControl() {
            return new CacheControlEphemeral();
        }

        internal static MessageCreateParamsSystem BuildSystemPrompt(string systemPrompt, bool enablePromptCaching) {
            if (!enablePromptCaching) {
                return systemPrompt;
            }

            return new MessageCreateParamsSystem([
                new TextBlockParam(systemPrompt) {
                    CacheControl = CreateCacheControl(),
                }
            ], null);
        }

        private static MessageParam CloneMessageForCachePrefix(MessageParam message, bool markCacheControl) {
            if (message.Content.TryPickString(out var text)) {
                return new MessageParam {
                    Role = message.Role,
                    Content = new List<ContentBlockParam> {
                        new(new TextBlockParam(text) {
                            CacheControl = markCacheControl ? CreateCacheControl() : null,
                        }, null)
                    }
                };
            }

            if (!message.Content.TryPickContentBlockParams(out var blocks)) {
                return new MessageParam {
                    Role = message.Role,
                    Content = message.Content,
                };
            }

            var clonedBlocks = new List<ContentBlockParam>();
            for (int i = 0; i < blocks.Count; i++) {
                var isLastBlock = markCacheControl && i == blocks.Count - 1;
                var block = blocks[i];
                if (block.TryPickText(out var textBlock)) {
                    clonedBlocks.Add(new ContentBlockParam(new TextBlockParam(textBlock.Text) {
                        CacheControl = isLastBlock ? CreateCacheControl() : textBlock.CacheControl,
                        Citations = textBlock.Citations,
                    }, null));
                    continue;
                }

                if (block.TryPickImage(out var imageBlock)) {
                    clonedBlocks.Add(new ContentBlockParam(new ImageBlockParam {
                        Source = imageBlock.Source,
                        CacheControl = isLastBlock ? CreateCacheControl() : imageBlock.CacheControl,
                    }, null));
                    continue;
                }

                clonedBlocks.Add(block);
            }

            return new MessageParam {
                Role = message.Role,
                Content = clonedBlocks,
            };
        }

        internal static List<MessageParam> PrepareMessagesForPromptCaching(List<MessageParam> providerHistory, bool enablePromptCaching, bool excludeDynamicTail, out bool cacheBreakpointInserted) {
            cacheBreakpointInserted = enablePromptCaching;
            if (!enablePromptCaching || providerHistory.Count == 0) {
                return providerHistory;
            }

            var stableCount = excludeDynamicTail
                ? Math.Max(providerHistory.Count - 1, 0)
                : providerHistory.Count;
            if (stableCount == 0) {
                return providerHistory;
            }

            var clonedMessages = providerHistory
                .Select((message, index) => CloneMessageForCachePrefix(message, index == stableCount - 1))
                .ToList();
            cacheBreakpointInserted = true;
            return clonedMessages;
        }

        internal static (string toolDefinitionHash, string stablePrefixHash) BuildPromptCachingContext(string mode, string systemPrompt, List<SerializedChatMessage> stableHistory) {
            var toolDefinitionHash = PromptCachingHelper.ComputeToolDefinitionHash();
            var stablePrefixHash = PromptCachingHelper.ComputeStablePrefixHash(new {
                Mode = mode,
                SystemPrompt = systemPrompt,
                StableHistory = stableHistory,
            });
            return (toolDefinitionHash, stablePrefixHash);
        }

        private void LogPromptCachingObservation(
            LLMChannel channel,
            string providerName,
            string modelName,
            bool promptCachingEnabled,
            string toolDefinitionHash,
            string stablePrefixHash,
            bool cacheBreakpointInserted,
            long? cacheCreationInputTokens,
            long? cacheReadInputTokens,
            object usage) {
            var usageJson = usage == null ? null : JsonConvert.SerializeObject(usage);
            var observationKey = $"{providerName}:{modelName}:{stablePrefixHash}:{toolDefinitionHash}";
            var outcome = PromptCachingHelper.DetermineAnthropicOutcome(
                promptCachingEnabled,
                cacheBreakpointInserted,
                observationKey,
                cacheCreationInputTokens,
                cacheReadInputTokens,
                out var missReason);

            PromptCachingHelper.LogObservation(_logger, new PromptCachingObservation {
                Provider = providerName,
                ChannelId = channel.Id,
                Model = modelName,
                PromptCachingEnabled = promptCachingEnabled,
                StablePrefixHash = stablePrefixHash,
                ToolDefinitionHash = toolDefinitionHash,
                CacheOutcome = outcome,
                MissReason = missReason,
                CacheBreakpointInserted = cacheBreakpointInserted,
                CacheCreationInputTokens = cacheCreationInputTokens,
                CacheReadInputTokens = cacheReadInputTokens,
                ProviderUsageJson = usageJson,
            });
        }

        #region Models

        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            var discovered = await TryDiscoverModelsAsync(channel);
            if (discovered.Count > 0) {
                return discovered;
            }

            _logger.LogInformation("Anthropic 目录发现失败或为空，回退到内置静态快照（{Count} 个模型）", _anthropicModels.Length);
            return _anthropicModels;
        }

        /// <summary>
        /// Anthropic-compatible GET /v1/models 发现（官方 API 与 OpenCode 等兼容网关都提供）；
        /// 任何失败都返回空列表，由调用方回退到静态快照，刷新链条不会因单个网关不可用而中断。
        /// </summary>
        private async Task<List<string>> TryDiscoverModelsAsync(LLMChannel channel) {
            var result = new List<string>();
            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway)) {
                return result;
            }

            try {
                var baseUrl = BuildModelsBaseUrl(channel.Gateway);
                using var httpClient = _httpClientFactory.CreateClient();
                if (!string.IsNullOrWhiteSpace(channel.ApiKey)) {
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", channel.ApiKey);
                }
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                OpencodeSessionHeaders.Apply(httpClient, channel.Gateway);

                using var response = await httpClient.GetAsync($"{baseUrl}/models?limit=100");
                if (!response.IsSuccessStatusCode) {
                    _logger.LogWarning("Anthropic 目录请求失败 {StatusCode} ({Url})", (int)response.StatusCode, baseUrl);
                    return result;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) {
                    return result;
                }

                foreach (var item in data.EnumerateArray()) {
                    var id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(id)) {
                        result.Add(id!);
                    }
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Anthropic 目录请求异常，回退到静态快照");
            }

            return result;
        }

        /// <summary>把渠道网关归一化为 GET /v1/models 的基地址（重复的 /v1 只保留一次）。</summary>
        internal static string BuildModelsBaseUrl(string gateway) {
            var trimmed = gateway.TrimEnd('/');
            return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + "/v1";
        }

        public virtual async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel) {
            var results = new List<ModelWithCapabilities>();
            foreach (var modelName in await GetAllModels(channel)) {
                results.Add(InferAnthropicModelCapabilities(modelName));
            }
            return results;
        }

        private ModelWithCapabilities InferAnthropicModelCapabilities(string modelName) {
            var model = new ModelWithCapabilities { ModelName = modelName };
            var lowerName = modelName.ToLower();

            model.SetCapability("streaming", true);
            model.SetCapability("function_calling", true);
            model.SetCapability("tool_calls", true);
            model.SetCapability("chat", true);
            model.SetCapability("model_family", "Claude");

            // All Claude 3+ models support vision
            model.SetCapability("vision", true);
            model.SetCapability("image_content", true);

            // Claude 3.5+ and Claude 4+ are multimodal
            if (lowerName.Contains("claude-sonnet-4") || lowerName.Contains("claude-opus-4") ||
                lowerName.Contains("claude-3-5") || lowerName.Contains("claude-3.5")) {
                model.SetCapability("multimodal", true);
                model.SetCapability("advanced_reasoning", true);
            }

            if (lowerName.Contains("opus")) {
                model.SetCapability("advanced_reasoning", true);
                model.SetCapability("complex_tasks", true);
            }

            if (lowerName.Contains("haiku")) {
                model.SetCapability("fast_response", true);
                model.SetCapability("optimized", true);
            }

            return model;
        }

        #endregion
        private static string ExtractTextFromContent(MessageParamContent content) {
            if (content.TryPickString(out var text)) {
                return text;
            }
            if (content.TryPickContentBlockParams(out var blocks)) {
                var sb = new StringBuilder();
                foreach (var block in blocks) {
                    if (block.TryPickText(out var textBlock)) {
                        sb.Append(textBlock.Text);
                    }
                }
                return sb.ToString();
            }
            return content.ToString();
        }

        internal static List<MessageParam> EnsureAlternatingRoles(List<MessageParam> messages) {
            if (!messages.Any()) return messages;

            var result = new List<MessageParam>();

            foreach (var msg in messages) {
                if (result.Count > 0 && result.Last().Role == msg.Role) {
                    // Merge with previous message of same role
                    var prev = result.Last();
                    var prevContent = ExtractTextFromContent(prev.Content);
                    var curContent = ExtractTextFromContent(msg.Content);
                    result[result.Count - 1] = new MessageParam {
                        Role = prev.Role,
                        Content = prevContent + "\n" + curContent
                    };
                } else {
                    result.Add(msg);
                }
            }

            // Ensure starts with user
            if (result.Any() && result.First().Role == Role.Assistant) {
                result.Insert(0, new MessageParam {
                    Role = Role.User,
                    Content = "(conversation start)"
                });
            }

            return result;
        }


        #region Vision Support

        /// <summary>
        /// 检查模型是否支持视觉能力
        /// </summary>

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

        #endregion

        #region Tool Conversion

        /// <summary>
        /// Convert OpenAI ChatTool definitions to Anthropic Tool objects.
        /// </summary>
        private static List<ToolUnion> ConvertToAnthropicTools(List<OpenAI.Chat.ChatTool> openAiTools, bool enablePromptCaching) {
            var tools = new List<ToolUnion>();
            for (int index = 0; index < openAiTools.Count; index++) {
                var chatTool = openAiTools[index];
                try {
                    var toolName = chatTool.FunctionName;
                    var toolDescription = chatTool.FunctionDescription;
                    var paramsJson = chatTool.FunctionParameters?.ToString() ?? "{}";
                    var schemaDoc = System.Text.Json.JsonDocument.Parse(paramsJson);
                    var root = schemaDoc.RootElement;

                    var properties = new Dictionary<string, JsonElement>();
                    var required = new List<string>();

                    if (root.TryGetProperty("properties", out var propsEl) && propsEl.ValueKind == JsonValueKind.Object) {
                        foreach (var prop in propsEl.EnumerateObject()) {
                            properties[prop.Name] = prop.Value.Clone();
                        }
                    }

                    if (root.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array) {
                        foreach (var item in reqEl.EnumerateArray()) {
                            required.Add(item.GetString());
                        }
                    }

                    var inputSchema = new InputSchema {
                        Type = System.Text.Json.JsonDocument.Parse("\"object\"").RootElement,
                        Properties = properties,
                        Required = required
                    };

                    tools.Add(new ToolUnion(new Tool {
                        Name = toolName,
                        Description = toolDescription,
                        InputSchema = inputSchema,
                        CacheControl = enablePromptCaching && index == openAiTools.Count - 1 ? CreateCacheControl() : null,
                    }, null));
                } catch (Exception) {
                }
            }

            return tools;
        }

        #endregion

        /// <summary>
        /// Serialize Anthropic message list to portable format for snapshot persistence.
        /// </summary>
        public static List<SerializedChatMessage> SerializeProviderHistory(string systemPrompt, List<MessageParam> history) {
            var result = new List<SerializedChatMessage>();

            if (!string.IsNullOrWhiteSpace(systemPrompt)) {
                result.Add(new SerializedChatMessage { Role = "system", Content = systemPrompt });
            }

            foreach (var msg in history) {
                string role = msg.Role == Role.Assistant ? "assistant" : "user";
                string content = ExtractTextFromContent(msg.Content);
                result.Add(new SerializedChatMessage { Role = role, Content = content });
            }

            return result;
        }

        /// <summary>
        /// Deserialize portable format back to Anthropic message list.
        /// Returns (systemPrompt, messages).
        /// </summary>
        public static (string systemPrompt, List<MessageParam> messages) DeserializeProviderHistory(List<SerializedChatMessage> serialized) {
            string systemPrompt = null;
            var messages = new List<MessageParam>();
            if (serialized == null) return (systemPrompt, messages);

            foreach (var msg in serialized) {
                switch (msg.Role?.ToLowerInvariant()) {
                    case "system":
                        systemPrompt = msg.Content ?? "";
                        break;
                    case "assistant":
                        messages.Add(new MessageParam {
                            Role = Role.Assistant,
                            Content = msg.Content ?? ""
                        });
                        break;
                    case "user":
                    default:
                        messages.Add(new MessageParam {
                            Role = Role.User,
                            Content = msg.Content ?? ""
                        });
                        break;
                }
            }

            messages = EnsureAlternatingRoles(messages);
            return (systemPrompt, messages);
        }

        #region Embeddings

        public Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel) {
            throw new NotSupportedException($"{ServiceName}: Anthropic does not natively support embeddings. Use a different provider for embedding generation.");
        }

        #endregion

        #region Image Analysis

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, string prompt = null) {
            return await AnalyzeImageAsync(photoPath, modelName, channel, null, prompt);
        }

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, LLMApiBinding binding, string prompt = null) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                modelName = "claude-sonnet-4-20250514";
            }

            prompt = string.IsNullOrWhiteSpace(prompt) ? GeneralLLMService.DefaultAltPhotoPrompt : prompt;

            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel or ApiKey is not configured.", ServiceName);
                return $"Error: {ServiceName} channel/apikey is not configured.";
            }

            using var client = CreateClient(channel, binding);

            try {
                using var fileStream = File.OpenRead(photoPath);
                var bitmap = SKBitmap.Decode(fileStream);
                var imgData = bitmap.Encode(SKEncodedImageFormat.Png, 99);
                var imgArray = imgData.ToArray();
                var base64Image = Convert.ToBase64String(imgArray);

                var imageSource = new Base64ImageSource {
                    Data = base64Image,
                    MediaType = MediaType.ImagePng,
                };

                var contentBlocks = new List<ContentBlockParam> {
                    new ImageBlockParam(imageSource),
                    new TextBlockParam(prompt),
                };

                var messages = new List<MessageParam> {
                    new MessageParam {
                        Role = Role.User,
                        Content = contentBlocks
                    }
                };

                var parameters = new MessageCreateParams {
                    Model = modelName,
                    MaxTokens = 4096,
                    Messages = messages,
                };

                var responseBuilder = new StringBuilder();
                await foreach (var rawEvent in client.Messages.CreateStreaming(parameters)) {
                    if (rawEvent.TryPickContentBlockDelta(out var deltaEvent)) {
                        if (deltaEvent.Delta.TryPickText(out var textDelta)) {
                            responseBuilder.Append(textDelta.Text);
                        }
                    }
                }

                return responseBuilder.ToString();
            } catch (Exception ex) {
                _logger.LogError(ex, "Error analyzing image with Anthropic");
                return $"Error analyzing image: {ex.Message}";
            }
        }

        #endregion
    }
}

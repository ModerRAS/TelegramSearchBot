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
    public class AnthropicService : IService, ILLMService {
        public string ServiceName => "AnthropicService";

        private readonly ILogger<AnthropicService> _logger;
        private readonly DataDbContext _dbContext;
        private readonly IMessageExtensionService _messageExtensionService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IBotIdentityProvider _botIdentityProvider;
        private readonly LlmVisibilityService _llmVisibilityService;
        private readonly PromptCachingSettingsService _promptCachingSettingsService;
        private string _fallbackBotName = string.Empty;

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

        private static readonly string[] _anthropicModels = {
            "claude-sonnet-4-20250514",
            "claude-opus-4-20250514",
            "claude-3-5-sonnet-20241022",
            "claude-3-5-haiku-20241022",
            "claude-3-opus-20240229",
            "claude-3-sonnet-20240229",
            "claude-3-haiku-20240307"
        };

        public AnthropicService(
            DataDbContext context,
            ILogger<AnthropicService> logger,
            IMessageExtensionService messageExtensionService,
            IHttpClientFactory httpClientFactory)
            : this(context, logger, messageExtensionService, httpClientFactory, null, null, null) {
        }

        public AnthropicService(
            DataDbContext context,
            ILogger<AnthropicService> logger,
            IMessageExtensionService messageExtensionService,
            IHttpClientFactory httpClientFactory,
            IBotIdentityProvider botIdentityProvider,
            LlmVisibilityService llmVisibilityService = null,
            PromptCachingSettingsService promptCachingSettingsService = null) {
            _logger = logger;
            _dbContext = context;
            _messageExtensionService = messageExtensionService;
            _httpClientFactory = httpClientFactory;
            _botIdentityProvider = botIdentityProvider;
            _llmVisibilityService = llmVisibilityService;
            _promptCachingSettingsService = promptCachingSettingsService;
            _logger.LogInformation("AnthropicService instance created. McpToolHelper should be initialized at application startup.");
        }

        private async Task<string> GetBotNameAsync() {
            if (_botIdentityProvider == null) {
                return _fallbackBotName;
            }

            var identity = await _botIdentityProvider.GetIdentityAsync();
            return identity.UserName ?? string.Empty;
        }

        private AnthropicClient CreateClient(LLMChannel channel, LLMApiBinding? binding = null) {
            var options = new Anthropic.Core.ClientOptions {
                ApiKey = LlmBindingSupport.ResolveApiKey(channel, binding),
            };
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            if (!string.IsNullOrWhiteSpace(endpoint)) {
                // Binding URL 已含 /v1（如 https://opencode.ai/zen/v1），SDK 会再追加 /v1/messages；
                // 剥离尾部 /v1 使 SDK 追加后命中精确 binding 路径。legacy channel.Gateway 保持字节一致。
                if (binding != null && endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
                    endpoint = endpoint.Substring(0, endpoint.Length - 3);
                }
                options.BaseUrl = endpoint.TrimEnd('/');
            }
            return new AnthropicClient(options);
        }

        private async Task<bool> IsPromptCachingEnabledAsync() {
            return _promptCachingSettingsService == null || await _promptCachingSettingsService.IsEnabledAsync();
        }

        private static CacheControlEphemeral CreateCacheControl() {
            return new CacheControlEphemeral();
        }

        private static MessageCreateParamsSystem BuildSystemPrompt(string systemPrompt, bool enablePromptCaching) {
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

        private static List<MessageParam> PrepareMessagesForPromptCaching(List<MessageParam> providerHistory, bool enablePromptCaching, bool excludeDynamicTail, out bool cacheBreakpointInserted) {
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

        private static (string toolDefinitionHash, string stablePrefixHash) BuildPromptCachingContext(string mode, string systemPrompt, List<SerializedChatMessage> stableHistory) {
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

        public virtual Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            return Task.FromResult<IEnumerable<string>>(_anthropicModels);
        }

        public virtual Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel) {
            var results = new List<ModelWithCapabilities>();
            foreach (var modelName in _anthropicModels) {
                results.Add(InferAnthropicModelCapabilities(modelName));
            }
            return Task.FromResult<IEnumerable<ModelWithCapabilities>>(results);
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

        #region Chat History

        public bool IsSameSender(DataMessage message1, DataMessage message2) {
            if (message1 == null || message2 == null) return false;
            bool msg1IsBot = message1.FromUserId == Env.BotId;
            bool msg2IsBot = message2.FromUserId == Env.BotId;
            return msg1IsBot == msg2IsBot;
        }

        /// <summary>
        /// Build Anthropic message list from DB history. Anthropic requires alternating user/assistant roles.
        /// Returns (systemPrompt, messages).
        /// </summary>
        public async Task<(string systemPrompt, List<MessageParam> messages)> GetChatHistory(
            long chatId, string systemPrompt, DataMessage inputMessage = null) {
            return await GetChatHistory(chatId, systemPrompt, inputMessage, false);
        }

        public async Task<(string systemPrompt, List<MessageParam> messages)> GetChatHistory(
            long chatId, string systemPrompt, DataMessage inputMessage, bool supportsVision) {
            var dbMessages = await _dbContext.Messages.AsNoTracking()
                .Where(m => m.GroupId == chatId && m.DateTime > DateTime.UtcNow.AddHours(-1))
                .OrderBy(m => m.DateTime)
                .ToListAsync();

            if (dbMessages.Count < 10) {
                dbMessages = await _dbContext.Messages.AsNoTracking()
                    .Where(m => m.GroupId == chatId)
                    .OrderByDescending(m => m.DateTime)
                    .Take(10)
                    .OrderBy(m => m.DateTime)
                    .ToListAsync();
            }

            if (_llmVisibilityService != null) {
                dbMessages = await _llmVisibilityService.FilterVisibleMessagesAsync(chatId, dbMessages);
            }

            if (inputMessage != null &&
                ( _llmVisibilityService == null ||
                  !await _llmVisibilityService.IsUserInvisibleAsync(chatId, inputMessage.FromUserId) )) {
                dbMessages.Add(inputMessage);
            }

            _logger.LogInformation("Anthropic GetChatHistory: Found {Count} messages for ChatId {ChatId}.", dbMessages.Count, chatId);

            var result = new List<MessageParam>();
            var str = new StringBuilder();
            DataMessage previous = null;
            var userCache = new Dictionary<long, UserData>();
            var pendingImages = new List<byte[]>();

            foreach (var message in dbMessages) {
                // Skip leading bot messages (Anthropic messages must start with user)
                if (previous == null && !result.Any() && message.FromUserId == Env.BotId) {
                    previous = message;
                    continue;
                }

                if (previous != null && !IsSameSender(previous, message)) {
                    AddMessageToHistory(result, previous.FromUserId, str.ToString(), supportsVision ? pendingImages : null);
                    str.Clear();
                    pendingImages.Clear();
                }

                str.Append($"[{message.DateTime:yyyy-MM-dd HH:mm:ss zzz}]");
                if (message.FromUserId != 0) {
                    if (!userCache.TryGetValue(message.FromUserId, out var fromUser)) {
                        fromUser = await _dbContext.UserData.AsNoTracking()
                            .FirstOrDefaultAsync(u => u.Id == message.FromUserId);
                        if (fromUser != null) userCache[message.FromUserId] = fromUser;
                    }
                    str.Append(fromUser != null ? $"{fromUser.FirstName} {fromUser.LastName}".Trim() : $"User({message.FromUserId})");
                } else {
                    str.Append("System/Unknown");
                }

                if (message.ReplyToMessageId != 0) {
                    str.Append($"（Reply to msg {message.ReplyToMessageId}）");
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
                AddMessageToHistory(result, previous.FromUserId, str.ToString(), supportsVision ? pendingImages : null);
            }

            // Ensure messages alternate user/assistant and start with user
            result = EnsureAlternatingRoles(result);

            return (systemPrompt, result);
        }

        private void AddMessageToHistory(List<MessageParam> history, long fromUserId, string content) {
            AddMessageToHistory(history, fromUserId, content, null);
        }

        private void AddMessageToHistory(List<MessageParam> history, long fromUserId, string content, List<byte[]> images) {
            if (string.IsNullOrWhiteSpace(content) && ( images == null || images.Count == 0 )) return;
            if (!string.IsNullOrWhiteSpace(content)) {
                content = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\n{3,}", "\n\n");
            }

            var role = fromUserId == Env.BotId ? Role.Assistant : Role.User;

            if (images != null && images.Count > 0 && role == Role.User) {
                var contentBlocks = new List<ContentBlockParam>();
                if (!string.IsNullOrWhiteSpace(content)) {
                    contentBlocks.Add(new TextBlockParam(content.Trim()));
                }
                foreach (var imageBytes in images) {
                    contentBlocks.Add(new ImageBlockParam {
                        Source = new Base64ImageSource {
                            Data = Convert.ToBase64String(imageBytes),
                            MediaType = MediaType.ImagePng
                        }
                    });
                }
                history.Add(new MessageParam {
                    Role = role,
                    Content = contentBlocks
                });
            } else {
                history.Add(new MessageParam {
                    Role = role,
                    Content = content?.Trim() ?? ""
                });
            }
        }

        /// <summary>
        /// Ensures message list starts with user and alternates between user/assistant.
        /// Merges consecutive same-role messages.
        /// </summary>
        private static List<MessageParam> EnsureAlternatingRoles(List<MessageParam> messages) {
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

        #endregion

        #region Vision Support

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

        #region ExecAsync

        public async IAsyncEnumerable<string> ExecAsync(
            DataMessage message, long ChatId, string modelName, LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            var executionContext = new LlmExecutionContext();
            await foreach (var item in ExecAsync(message, ChatId, modelName, channel, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        public async IAsyncEnumerable<string> ExecAsync(
            DataMessage message, long ChatId, string modelName, LLMChannel channel,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var item in ExecAsync(message, ChatId, modelName, channel, null, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        public async IAsyncEnumerable<string> ExecAsync(
            DataMessage message, long ChatId, string modelName, LLMChannel channel,
            LLMApiBinding binding,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(modelName)) modelName = "claude-sonnet-4-20250514";

            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel or ApiKey is not configured.", ServiceName);
                yield return $"Error: {ServiceName} channel/apikey is not configured.";
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
                var nativeEnumerator = ExecWithNativeToolCallingAsync(message, ChatId, modelName, channel, binding, executionContext, nativeTools, cancellationToken);
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
            await foreach (var item in ExecWithXmlToolCallingAsync(message, ChatId, modelName, channel, binding, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        private static bool IsToolCallingNotSupportedError(Exception ex) {
            var message = ex.Message ?? "";
            return message.Contains("tools", StringComparison.OrdinalIgnoreCase) &&
                   ( message.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("invalid", StringComparison.OrdinalIgnoreCase) );
        }

        /// <summary>
        /// Execute LLM with native Anthropic tool calling API.
        /// </summary>
        private async IAsyncEnumerable<string> ExecWithNativeToolCallingAsync(
            DataMessage message, long ChatId, string modelName, LLMChannel channel,
            LLMApiBinding binding,
            LlmExecutionContext executionContext,
            List<OpenAI.Chat.ChatTool> nativeTools,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();

            var botName = await GetBotNameAsync();
            string systemPrompt = McpToolHelper.FormatSystemPromptForNativeToolCalling(botName, ChatId);
            bool supportsVision = await CheckVisionSupport(modelName, channel.Id);
            var (_, providerHistory) = await GetChatHistory(ChatId, systemPrompt, message, supportsVision);
            var promptCachingEnabled = await IsPromptCachingEnabledAsync();
            var stableHistory = providerHistory.Count > 0
                ? providerHistory.Take(providerHistory.Count - 1).ToList()
                : new List<MessageParam>();
            var (toolDefinitionHash, stablePrefixHash) = BuildPromptCachingContext(
                "anthropic-native",
                systemPrompt,
                SerializeProviderHistory(systemPrompt, stableHistory));
            providerHistory = PrepareMessagesForPromptCaching(providerHistory, promptCachingEnabled, excludeDynamicTail: true, out var cacheBreakpointInserted);

            using var client = CreateClient(channel, binding);
            var anthropicTools = ConvertToAnthropicTools(nativeTools, promptCachingEnabled);

            int maxToolCycles = Env.MaxToolCycles;
            var currentMessageContentBuilder = new StringBuilder();
            var trackedHistory = new List<SerializedChatMessage>();
            trackedHistory.Add(new SerializedChatMessage { Role = "system", Content = systemPrompt });

            for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                var parameters = new MessageCreateParams {
                    Model = modelName,
                    MaxTokens = 8192,
                    System = BuildSystemPrompt(systemPrompt, promptCachingEnabled),
                    Messages = providerHistory,
                    Tools = anthropicTools,
                };

                var contentBuilder = new StringBuilder();
                var toolUseBlocks = new List<(string id, string name, string inputJson)>();
                var currentToolInputBuilder = new StringBuilder();
                string currentToolId = null;
                string currentToolName = null;
                bool hasToolUse = false;
                long? cacheCreationInputTokens = null;
                long? cacheReadInputTokens = null;
                object usageObservation = null;

                await foreach (var rawEvent in client.Messages.CreateStreaming(parameters, cancellationToken)) {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    if (rawEvent.TryPickStart(out var messageStartEvent) && messageStartEvent.Message?.Usage != null) {
                        usageObservation = new {
                            messageStartEvent.Message.Usage.InputTokens,
                            messageStartEvent.Message.Usage.OutputTokens,
                            messageStartEvent.Message.Usage.CacheCreationInputTokens,
                            messageStartEvent.Message.Usage.CacheReadInputTokens,
                            RawData = messageStartEvent.Message.Usage.RawData,
                        };
                    }

                    if (rawEvent.TryPickDelta(out var messageDeltaEvent) && messageDeltaEvent.Usage != null) {
                        cacheCreationInputTokens = messageDeltaEvent.Usage.CacheCreationInputTokens ?? cacheCreationInputTokens;
                        cacheReadInputTokens = messageDeltaEvent.Usage.CacheReadInputTokens ?? cacheReadInputTokens;
                        usageObservation = new {
                            messageDeltaEvent.Usage.InputTokens,
                            messageDeltaEvent.Usage.OutputTokens,
                            messageDeltaEvent.Usage.CacheCreationInputTokens,
                            messageDeltaEvent.Usage.CacheReadInputTokens,
                            RawData = messageDeltaEvent.Usage.RawData,
                        };
                    }

                    if (rawEvent.TryPickContentBlockStart(out var startEvent)) {
                        if (startEvent.ContentBlock.TryPickToolUse(out var toolUseStart)) {
                            currentToolId = toolUseStart.ID;
                            currentToolName = toolUseStart.Name;
                            currentToolInputBuilder.Clear();
                            hasToolUse = true;
                        }
                    } else if (rawEvent.TryPickContentBlockDelta(out var deltaEvent)) {
                        if (deltaEvent.Delta.TryPickText(out var textDelta)) {
                            contentBuilder.Append(textDelta.Text);
                            currentMessageContentBuilder.Append(textDelta.Text);
                            if (currentMessageContentBuilder.Length > 10) {
                                yield return currentMessageContentBuilder.ToString();
                            }
                        } else if (deltaEvent.Delta.TryPickInputJson(out var inputJsonDelta)) {
                            currentToolInputBuilder.Append(inputJsonDelta.PartialJson);
                        }
                    } else if (rawEvent.TryPickContentBlockStop(out _)) {
                        if (currentToolId != null) {
                            toolUseBlocks.Add((currentToolId, currentToolName, currentToolInputBuilder.ToString()));
                            currentToolId = null;
                            currentToolName = null;
                        }
                    }
                }

                LogPromptCachingObservation(
                    channel,
                    "Anthropic",
                    modelName,
                    promptCachingEnabled,
                    toolDefinitionHash,
                    stablePrefixHash,
                    cacheBreakpointInserted,
                    cacheCreationInputTokens,
                    cacheReadInputTokens,
                    usageObservation);

                string responseText = contentBuilder.ToString().Trim();

                if (hasToolUse && toolUseBlocks.Any()) {
                    // Build assistant message with text + tool use blocks
                    var assistantContentBlocks = new List<ContentBlockParam>();
                    if (!string.IsNullOrWhiteSpace(responseText)) {
                        assistantContentBlocks.Add(new TextBlockParam(responseText));
                    }
                    foreach (var (id, name, inputJson) in toolUseBlocks) {
                        Dictionary<string, JsonElement> parsedInput;
                        try {
                            parsedInput = string.IsNullOrWhiteSpace(inputJson)
                                ? new Dictionary<string, JsonElement>()
                                : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputJson);
                        } catch (Exception ex) {
                            _logger.LogError(
                                ex,
                                "{ServiceName}: Failed to deserialize Anthropic native tool input for assistant history. ToolUseId={ToolUseId}, ToolName={ToolName}, InputJson={InputJson}, ErrorSummary={ErrorSummary}",
                                ServiceName,
                                id,
                                name,
                                inputJson,
                                ex.GetLogSummary());
                            parsedInput = new Dictionary<string, JsonElement>();
                        }
                        assistantContentBlocks.Add(new ToolUseBlockParam {
                            ID = id,
                            Name = name,
                            Input = parsedInput
                        });
                    }

                    providerHistory.Add(new MessageParam {
                        Role = Role.Assistant,
                        Content = assistantContentBlocks
                    });

                    trackedHistory.Add(new SerializedChatMessage { Role = "assistant", Content = responseText });

                    var toolNamesBuilder = new StringBuilder();
                    foreach (var (id, name, inputJson) in toolUseBlocks) {
                        Dictionary<string, string> argsDict = null;
                        if (!string.IsNullOrWhiteSpace(inputJson)) {
                            try {
                                argsDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputJson)
                                    .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
                            } catch (Exception ex) {
                                _logger.LogError(
                                    ex,
                                    "{ServiceName}: Failed to deserialize Anthropic native tool input for display. ToolUseId={ToolUseId}, ToolName={ToolName}, InputJson={InputJson}, ErrorSummary={ErrorSummary}",
                                    ServiceName,
                                    id,
                                    name,
                                    inputJson,
                                    ex.GetLogSummary());
                            }
                        }
                        argsDict ??= new Dictionary<string, string>();
                        toolNamesBuilder.Append(McpToolHelper.FormatToolCallDisplay(name, argsDict));
                    }
                    currentMessageContentBuilder.Append(toolNamesBuilder.ToString());
                    yield return currentMessageContentBuilder.ToString();

                    // Execute tools and build tool result message
                    var toolResultBlocks = new List<ContentBlockParam>();
                    foreach (var (id, name, inputJson) in toolUseBlocks) {
                        _logger.LogInformation("{ServiceName}: Native tool call: {ToolName} with arguments: {Arguments}", ServiceName, name, inputJson);

                        string toolResultString;
                        bool isError = false;
                        try {
                            var argsDict = string.IsNullOrWhiteSpace(inputJson)
                                ? new Dictionary<string, string>()
                                : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputJson)
                                    .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

                            var toolContext = new ToolContext { ChatId = ChatId, UserId = message.FromUserId, MessageId = message.MessageId };
                            object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(name, argsDict, toolContext);
                            toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                            _logger.LogInformation("{ServiceName}: Tool {ToolName} executed. Result length: {Length}", ServiceName, name, toolResultString.Length);
                        } catch (Exception ex) {
                            isError = true;
                            _logger.LogError(
                                ex,
                                "{ServiceName}: Error executing Anthropic native tool {ToolName}. ToolUseId={ToolUseId}, InputJson={InputJson}, ErrorSummary={ErrorSummary}",
                                ServiceName,
                                name,
                                id,
                                inputJson,
                                ex.GetLogSummary());
                            toolResultString = $"Error executing tool {name}: {ex.GetLogSummary()}";
                        }

                        toolResultBlocks.Add(new ToolResultBlockParam(id) {
                            Content = toolResultString,
                            IsError = isError,
                        });
                    }

                    providerHistory.Add(new MessageParam {
                        Role = Role.User,
                        Content = toolResultBlocks
                    });
                } else {
                    // Regular text response, no tool calls
                    if (!string.IsNullOrWhiteSpace(responseText)) {
                        trackedHistory.Add(new SerializedChatMessage { Role = "assistant", Content = responseText });
                    }
                    yield break;
                }
            }

            _logger.LogWarning("{ServiceName}: Max tool call cycles reached for chat {ChatId}. User confirmation needed.", ServiceName, ChatId);
            if (executionContext != null) {
                executionContext.IterationLimitReached = true;
                executionContext.SnapshotData = new LlmContinuationSnapshot {
                    SchemaVersion = LlmContinuationSnapshot.CurrentSchemaVersion,
                    ChatId = ChatId,
                    OriginalMessageId = message.MessageId,
                    UserId = message.FromUserId,
                    ModelName = modelName,
                    Provider = "Anthropic",
                    ChannelId = channel.Id,
                    LastAccumulatedContent = currentMessageContentBuilder.ToString(),
                    CyclesSoFar = maxToolCycles,
                    ProviderHistory = trackedHistory,
                };
            }
        }

        /// <summary>
        /// Execute LLM with XML prompt-based tool calling (fallback).
        /// </summary>
        private async IAsyncEnumerable<string> ExecWithXmlToolCallingAsync(
            DataMessage message, long ChatId, string modelName, LLMChannel channel,
            LLMApiBinding binding,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();

            var botName = await GetBotNameAsync();
            string systemPrompt = McpToolHelper.FormatSystemPrompt(botName, ChatId);
            bool supportsVision = await CheckVisionSupport(modelName, channel.Id);
            var (_, providerHistory) = await GetChatHistory(ChatId, systemPrompt, message, supportsVision);
            var promptCachingEnabled = await IsPromptCachingEnabledAsync();
            var stableHistory = providerHistory.Count > 0
                ? providerHistory.Take(providerHistory.Count - 1).ToList()
                : new List<MessageParam>();
            var (toolDefinitionHash, stablePrefixHash) = BuildPromptCachingContext(
                "anthropic-xml",
                systemPrompt,
                SerializeProviderHistory(systemPrompt, stableHistory));
            providerHistory = PrepareMessagesForPromptCaching(providerHistory, promptCachingEnabled, excludeDynamicTail: true, out var cacheBreakpointInserted);

            using var client = CreateClient(channel, binding);

            int maxToolCycles = Env.MaxToolCycles;
            var currentMessageContentBuilder = new StringBuilder();
            var trackedHistory = new List<SerializedChatMessage>();
            trackedHistory.Add(new SerializedChatMessage { Role = "system", Content = systemPrompt });

            for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                var parameters = new MessageCreateParams {
                    Model = modelName,
                    MaxTokens = 8192,
                    System = BuildSystemPrompt(systemPrompt, promptCachingEnabled),
                    Messages = providerHistory,
                };

                var llmResponseBuilder = new StringBuilder();
                long? cacheCreationInputTokens = null;
                long? cacheReadInputTokens = null;
                object usageObservation = null;

                await foreach (var rawEvent in client.Messages.CreateStreaming(parameters, cancellationToken)) {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    if (rawEvent.TryPickStart(out var messageStartEvent) && messageStartEvent.Message?.Usage != null) {
                        usageObservation = new {
                            messageStartEvent.Message.Usage.InputTokens,
                            messageStartEvent.Message.Usage.OutputTokens,
                            messageStartEvent.Message.Usage.CacheCreationInputTokens,
                            messageStartEvent.Message.Usage.CacheReadInputTokens,
                            RawData = messageStartEvent.Message.Usage.RawData,
                        };
                    }

                    if (rawEvent.TryPickDelta(out var messageDeltaEvent) && messageDeltaEvent.Usage != null) {
                        cacheCreationInputTokens = messageDeltaEvent.Usage.CacheCreationInputTokens ?? cacheCreationInputTokens;
                        cacheReadInputTokens = messageDeltaEvent.Usage.CacheReadInputTokens ?? cacheReadInputTokens;
                        usageObservation = new {
                            messageDeltaEvent.Usage.InputTokens,
                            messageDeltaEvent.Usage.OutputTokens,
                            messageDeltaEvent.Usage.CacheCreationInputTokens,
                            messageDeltaEvent.Usage.CacheReadInputTokens,
                            RawData = messageDeltaEvent.Usage.RawData,
                        };
                    }

                    if (rawEvent.TryPickContentBlockDelta(out var deltaEvent)) {
                        if (deltaEvent.Delta.TryPickText(out var textDelta)) {
                            currentMessageContentBuilder.Append(textDelta.Text);
                            llmResponseBuilder.Append(textDelta.Text);
                            if (currentMessageContentBuilder.Length > 10) {
                                yield return currentMessageContentBuilder.ToString();
                            }
                        }
                    }
                }

                LogPromptCachingObservation(
                    channel,
                    "Anthropic",
                    modelName,
                    promptCachingEnabled,
                    toolDefinitionHash,
                    stablePrefixHash,
                    cacheBreakpointInserted,
                    cacheCreationInputTokens,
                    cacheReadInputTokens,
                    usageObservation);

                string llmFullResponseText = llmResponseBuilder.ToString().Trim();
                _logger.LogDebug("{ServiceName} raw full response (Cycle {Cycle}): {Response}", ServiceName, cycle + 1, llmFullResponseText);

                trackedHistory.Add(new SerializedChatMessage { Role = "assistant", Content = llmFullResponseText });

                if (!string.IsNullOrWhiteSpace(llmFullResponseText)) {
                    providerHistory.Add(new MessageParam {
                        Role = Role.Assistant,
                        Content = llmFullResponseText
                    });
                } else if (cycle < maxToolCycles - 1) {
                    _logger.LogWarning("{ServiceName}: LLM returned empty response during tool cycle {Cycle}.", ServiceName, cycle + 1);
                }

                // XML tool parsing
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
                        _logger.LogError(
                            ex,
                            "{ServiceName}: Error executing Anthropic XML tool {ToolName}. Arguments={Arguments}, ErrorSummary={ErrorSummary}",
                            ServiceName,
                            parsedToolName,
                            JsonConvert.SerializeObject(toolArguments),
                            ex.GetLogSummary());
                        toolResultString = $"Error executing tool {parsedToolName}: {ex.GetLogSummary()}.";
                    }

                    string feedbackPrefix = isError ? $"[Tool '{parsedToolName}' Execution Failed. Error: " : $"[Executed Tool '{parsedToolName}'. Result: ";
                    string feedback = $"{feedbackPrefix}{toolResultString}]";

                    trackedHistory.Add(new SerializedChatMessage { Role = "user", Content = feedback });

                    providerHistory.Add(new MessageParam {
                        Role = Role.User,
                        Content = feedback
                    });
                    _logger.LogInformation("Added user feedback to history for LLM: {Feedback}", feedback);
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
                    SchemaVersion = LlmContinuationSnapshot.CurrentSchemaVersion,
                    ChatId = ChatId,
                    OriginalMessageId = message.MessageId,
                    UserId = message.FromUserId,
                    ModelName = modelName,
                    Provider = "Anthropic",
                    ChannelId = channel.Id,
                    LastAccumulatedContent = currentMessageContentBuilder.ToString(),
                    CyclesSoFar = maxToolCycles,
                    ProviderHistory = trackedHistory,
                };
            }
        }

        #endregion

        #region ResumeFromSnapshot

        public async IAsyncEnumerable<string> ResumeFromSnapshotAsync(
            LlmContinuationSnapshot snapshot, LLMChannel channel,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await foreach (var item in ResumeFromSnapshotAsync(snapshot, channel, null, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        public async IAsyncEnumerable<string> ResumeFromSnapshotAsync(
            LlmContinuationSnapshot snapshot, LLMChannel channel,
            LLMApiBinding binding,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            using var chatContentLogScope = LoggerHolders.PushChatContentLogScope();
            if (snapshot == null) {
                _logger.LogError("{ServiceName}: Cannot resume from null snapshot.", ServiceName);
                yield break;
            }
            var endpoint = LlmBindingSupport.ResolveEndpoint(channel, binding);
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            if (channel == null || string.IsNullOrWhiteSpace(endpoint) || (binding?.AuthProfile != LlmAuthProfile.None && string.IsNullOrWhiteSpace(apiKey))) {
                _logger.LogError("{ServiceName}: Channel or ApiKey is not configured for resume.", ServiceName);
                yield break;
            }

            var modelName = snapshot.ModelName;
            if (string.IsNullOrWhiteSpace(modelName)) modelName = "claude-sonnet-4-20250514";

            _logger.LogInformation("{ServiceName}: Resuming from snapshot {SnapshotId} for ChatId {ChatId}, restoring {HistoryCount} history entries.",
                ServiceName, snapshot.SnapshotId, snapshot.ChatId, snapshot.ProviderHistory?.Count ?? 0);

            var (systemPrompt, providerHistory) = DeserializeProviderHistory(snapshot.ProviderHistory);
            var promptCachingEnabled = await IsPromptCachingEnabledAsync();
            var (toolDefinitionHash, stablePrefixHash) = BuildPromptCachingContext(
                "anthropic-resume",
                systemPrompt ?? string.Empty,
                snapshot.ProviderHistory ?? []);
            providerHistory = PrepareMessagesForPromptCaching(providerHistory, promptCachingEnabled, excludeDynamicTail: false, out var cacheBreakpointInserted);

            using var client = CreateClient(channel, binding);

            var fullContentBuilder = new StringBuilder(snapshot.LastAccumulatedContent ?? "");
            var newContentBuilder = new StringBuilder();

            int maxToolCycles = Env.MaxToolCycles;
            var trackedHistory = snapshot.ProviderHistory ?? new List<SerializedChatMessage>();

            for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                var parameters = new MessageCreateParams {
                    Model = modelName,
                    MaxTokens = 8192,
                    System = BuildSystemPrompt(systemPrompt ?? string.Empty, promptCachingEnabled),
                    Messages = providerHistory,
                };

                var llmResponseBuilder = new StringBuilder();
                long? cacheCreationInputTokens = null;
                long? cacheReadInputTokens = null;
                object usageObservation = null;

                await foreach (var rawEvent in client.Messages.CreateStreaming(parameters, cancellationToken)) {
                    if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                    if (rawEvent.TryPickStart(out var messageStartEvent) && messageStartEvent.Message?.Usage != null) {
                        usageObservation = new {
                            messageStartEvent.Message.Usage.InputTokens,
                            messageStartEvent.Message.Usage.OutputTokens,
                            messageStartEvent.Message.Usage.CacheCreationInputTokens,
                            messageStartEvent.Message.Usage.CacheReadInputTokens,
                            RawData = messageStartEvent.Message.Usage.RawData,
                        };
                    }

                    if (rawEvent.TryPickDelta(out var messageDeltaEvent) && messageDeltaEvent.Usage != null) {
                        cacheCreationInputTokens = messageDeltaEvent.Usage.CacheCreationInputTokens ?? cacheCreationInputTokens;
                        cacheReadInputTokens = messageDeltaEvent.Usage.CacheReadInputTokens ?? cacheReadInputTokens;
                        usageObservation = new {
                            messageDeltaEvent.Usage.InputTokens,
                            messageDeltaEvent.Usage.OutputTokens,
                            messageDeltaEvent.Usage.CacheCreationInputTokens,
                            messageDeltaEvent.Usage.CacheReadInputTokens,
                            RawData = messageDeltaEvent.Usage.RawData,
                        };
                    }

                    if (rawEvent.TryPickContentBlockDelta(out var deltaEvent)) {
                        if (deltaEvent.Delta.TryPickText(out var textDelta)) {
                            fullContentBuilder.Append(textDelta.Text);
                            newContentBuilder.Append(textDelta.Text);
                            llmResponseBuilder.Append(textDelta.Text);
                            var newContent = newContentBuilder.ToString();
                            if (newContent.Length > 10) {
                                yield return newContent;
                            }
                        }
                    }
                }

                LogPromptCachingObservation(
                    channel,
                    "Anthropic",
                    modelName,
                    promptCachingEnabled,
                    toolDefinitionHash,
                    stablePrefixHash,
                    cacheBreakpointInserted,
                    cacheCreationInputTokens,
                    cacheReadInputTokens,
                    usageObservation);

                string llmFullResponseText = llmResponseBuilder.ToString().Trim();
                _logger.LogDebug("{ServiceName} raw full response (Resume Cycle {Cycle}): {Response}", ServiceName, cycle + 1, llmFullResponseText);

                if (!string.IsNullOrWhiteSpace(llmFullResponseText)) {
                    providerHistory.Add(new MessageParam {
                        Role = Role.Assistant,
                        Content = llmFullResponseText
                    });
                    trackedHistory.Add(new SerializedChatMessage { Role = "assistant", Content = llmFullResponseText });
                }

                if (McpToolHelper.TryParseToolCalls(llmFullResponseText, out var parsedToolCalls) && parsedToolCalls.Any()) {
                    var firstToolCall = parsedToolCalls[0];
                    string parsedToolName = firstToolCall.toolName;

                    _logger.LogInformation("{ServiceName}: LLM requested tool (resume): {ToolName}", ServiceName, parsedToolName);

                    var toolIndicator = McpToolHelper.FormatToolCallDisplay(parsedToolName, firstToolCall.arguments);
                    newContentBuilder.Append(toolIndicator);
                    fullContentBuilder.Append(toolIndicator);
                    yield return newContentBuilder.ToString();

                    string toolResultString;
                    bool isError = false;
                    try {
                        var toolContext = new ToolContext { ChatId = snapshot.ChatId, UserId = snapshot.UserId, MessageId = snapshot.OriginalMessageId };
                        object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(parsedToolName, firstToolCall.arguments, toolContext);
                        toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                    } catch (Exception ex) {
                        isError = true;
                        _logger.LogError(
                            ex,
                            "{ServiceName}: Error executing Anthropic XML tool {ToolName} (resume). Arguments={Arguments}, SnapshotId={SnapshotId}, ErrorSummary={ErrorSummary}",
                            ServiceName,
                            parsedToolName,
                            JsonConvert.SerializeObject(firstToolCall.arguments),
                            snapshot.SnapshotId,
                            ex.GetLogSummary());
                        toolResultString = $"Error executing tool {parsedToolName}: {ex.GetLogSummary()}.";
                    }

                    string feedbackPrefix = isError ? $"[Tool '{parsedToolName}' Execution Failed. Error: " : $"[Executed Tool '{parsedToolName}'. Result: ";
                    string feedback = $"{feedbackPrefix}{toolResultString}]";

                    providerHistory.Add(new MessageParam {
                        Role = Role.User,
                        Content = feedback
                    });
                    trackedHistory.Add(new SerializedChatMessage { Role = "user", Content = feedback });
                } else {
                    yield break;
                }
            }

            _logger.LogWarning("{ServiceName}: Max tool call cycles reached again during resume for ChatId {ChatId}.", ServiceName, snapshot.ChatId);
            if (executionContext != null) {
                executionContext.IterationLimitReached = true;
                executionContext.SnapshotData = new LlmContinuationSnapshot {
                    SchemaVersion = LlmContinuationSnapshot.CurrentSchemaVersion,
                    ChatId = snapshot.ChatId,
                    OriginalMessageId = snapshot.OriginalMessageId,
                    UserId = snapshot.UserId,
                    ModelName = modelName,
                    Provider = "Anthropic",
                    ChannelId = channel.Id,
                    LastAccumulatedContent = fullContentBuilder.ToString(),
                    CyclesSoFar = snapshot.CyclesSoFar + maxToolCycles,
                    ProviderHistory = trackedHistory,
                };
            }
        }

        #endregion

        #region Serialization

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

        #endregion

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

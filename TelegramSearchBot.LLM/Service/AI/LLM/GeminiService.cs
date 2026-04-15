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
    public class GeminiService : ILLMService, IService {
        public string ServiceName => "GeminiService";
        private readonly ILogger<GeminiService> _logger;
        private readonly DataDbContext _dbContext;
        private readonly Dictionary<long, ChatSession> _chatSessions = new();
        private readonly IHttpClientFactory _httpClientFactory;
        public string BotName { get; set; }

        public GeminiService(
            DataDbContext context,
            ILogger<GeminiService> logger,
            IHttpClientFactory httpClientFactory) {
            _logger = logger;
            _dbContext = context;
            _httpClientFactory = httpClientFactory;
            _logger.LogInformation("GeminiService instance created");
        }

        private void AddMessageToHistory(List<GenerativeAI.Types.Content> chatHistory, long fromUserId, string content) {
            AddMessageToHistory(chatHistory, fromUserId, content, null);
        }

        private void AddMessageToHistory(List<GenerativeAI.Types.Content> chatHistory, long fromUserId, string content, List<byte[]> images) {
            if (string.IsNullOrWhiteSpace(content) && (images == null || images.Count == 0)) return;
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

        public async Task<List<GenerativeAI.Types.Content>> GetChatHistory(long chatId, Message inputMessage = null) {
            return await GetChatHistory(chatId, inputMessage, false);
        }

        public async Task<List<GenerativeAI.Types.Content>> GetChatHistory(long chatId, Message inputMessage, bool supportsVision) {
            var messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == chatId && m.DateTime > DateTime.UtcNow.AddHours(-1))
                            .OrderBy(m => m.DateTime)
                            .ToListAsync();

            if (messages.Count < 10) {
                messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == chatId)
                            .OrderByDescending(m => m.DateTime)
                            .Take(10)
                            .OrderBy(m => m.DateTime)
                            .ToListAsync();
            }

            if (inputMessage != null) {
                messages.Add(inputMessage);
            }

            var chatHistory = new List<GenerativeAI.Types.Content>();
            var str = new StringBuilder();
            Message previous = null;
            var userCache = new Dictionary<long, UserData>();
            var pendingImages = new List<byte[]>();

            foreach (var message in messages) {
                if (previous == null && !chatHistory.Any() && message.FromUserId == Env.BotId) {
                    previous = message;
                    continue;
                }

                if (previous != null && ( previous.FromUserId == Env.BotId ) != ( message.FromUserId == Env.BotId )) {
                    AddMessageToHistory(chatHistory, previous.FromUserId, str.ToString(), supportsVision ? pendingImages : null);
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
                    str.Append($" (Reply to msg {message.ReplyToMessageId})");
                }
                str.Append(": ").Append(message.Content).Append("\n");

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
                AddMessageToHistory(chatHistory, previous.FromUserId, str.ToString(), supportsVision ? pendingImages : null);
            }

            return chatHistory;
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

        public async IAsyncEnumerable<string> ExecAsync(
            Message message,
            long ChatId,
            string modelName,
            LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            var executionContext = new LlmExecutionContext();
            await foreach (var item in ExecAsync(message, ChatId, modelName, channel, executionContext, cancellationToken)) {
                yield return item;
            }
        }

        public async IAsyncEnumerable<string> ExecAsync(
            Message message,
            long ChatId,
            string modelName,
            LLMChannel channel,
            LlmExecutionContext executionContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(modelName)) modelName = "gemini-1.5-flash";

            var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
            var model = googleAI.CreateGenerativeModel("models/" + modelName);
            var fullResponse = new StringBuilder();

            var history = await GetChatHistory(ChatId, message, await CheckVisionSupport(modelName, channel.Id));
            if (!_chatSessions.TryGetValue(ChatId, out var chatSession)) {
                chatSession = model.StartChat(history: history);
                _chatSessions[ChatId] = chatSession;
            }

            int maxToolCycles = Env.MaxToolCycles;
            var currentMessageBuilder = new StringBuilder();
            // Track history for snapshot
            var trackedHistory = new List<SerializedChatMessage>();
            trackedHistory.Add(new SerializedChatMessage { Role = "system", Content = McpToolHelper.FormatSystemPrompt(null, ChatId) });

            for (int cycle = 0; cycle < maxToolCycles; cycle++) {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                var fullResponseBuilder = new StringBuilder();
                trackedHistory.Add(new SerializedChatMessage { Role = "user", Content = message.Content });

                await foreach (var chunk in chatSession.StreamContentAsync(message.Content)) {
                    currentMessageBuilder.Append(chunk.Text);
                    fullResponseBuilder.Append(chunk.Text);
                    yield return currentMessageBuilder.ToString();
                }

                string llmResponse = fullResponseBuilder.ToString().Trim();
                _logger.LogDebug("Gemini raw response (Cycle {Cycle}): {Response}", cycle + 1, llmResponse);
                trackedHistory.Add(new SerializedChatMessage { Role = "assistant", Content = llmResponse });

                if (McpToolHelper.TryParseToolCalls(llmResponse, out var toolCalls) && toolCalls.Any()) {
                    var firstToolCall = toolCalls[0];
                    _logger.LogInformation("Gemini requested tool: {ToolName} with args: {Args}",
                        firstToolCall.toolName,
                        JsonConvert.SerializeObject(firstToolCall.arguments));

                    currentMessageBuilder.Append(McpToolHelper.FormatToolCallDisplay(firstToolCall.toolName, firstToolCall.arguments));
                    yield return currentMessageBuilder.ToString();

                    string toolResult;
                    bool isError = false;
                    try {
                        var toolContext = new ToolContext { ChatId = ChatId, UserId = message.FromUserId, MessageId = message.MessageId };
                        var result = await McpToolHelper.ExecuteRegisteredToolAsync(
                            firstToolCall.toolName,
                            firstToolCall.arguments,
                            toolContext);
                        toolResult = McpToolHelper.ConvertToolResultToString(result);
                    } catch (Exception ex) {
                        isError = true;
                        _logger.LogError(ex, "Error executing tool {ToolName}", firstToolCall.toolName);
                        toolResult = $"Error executing tool {firstToolCall.toolName}: {ex.Message}";
                    }

                    string feedback = isError
                        ? $"[Tool '{firstToolCall.toolName}' execution failed: {toolResult}]"
                        : $"[Tool '{firstToolCall.toolName}' result: {toolResult}]";

                    message.Content = feedback;
                    continue;
                }

                yield break;
            }

            _logger.LogWarning("{ServiceName}: Max tool call cycles reached for chat {ChatId}. User confirmation needed.", ServiceName, ChatId);
            if (executionContext != null) {
                executionContext.IterationLimitReached = true;
                executionContext.SnapshotData = new LlmContinuationSnapshot {
                    ChatId = ChatId,
                    OriginalMessageId = message.MessageId,
                    UserId = message.FromUserId,
                    ModelName = modelName,
                    Provider = "Gemini",
                    ChannelId = channel.Id,
                    LastAccumulatedContent = currentMessageBuilder.ToString(),
                    CyclesSoFar = maxToolCycles,
                    ProviderHistory = trackedHistory,
                };
            }
        }

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

        public async Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                modelName = "gpt-4-vision-preview";
            }

            if (channel == null || string.IsNullOrWhiteSpace(channel.Gateway) || string.IsNullOrWhiteSpace(channel.ApiKey)) {
                _logger.LogError("{ServiceName}: Channel, Gateway or ApiKey is not configured.", ServiceName);
                return $"Error: {ServiceName} channel/gateway/apikey is not configured.";
            }

            var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
            var model = googleAI.CreateGenerativeModel("models/" + modelName);
            try {
                var prompt = $"请根据这张图片生成一句准确、详尽的中文alt文本，说明画面中重要的元素、场景和含义，避免使用'图中显示'或'这是一张图片'这类通用表达。";

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

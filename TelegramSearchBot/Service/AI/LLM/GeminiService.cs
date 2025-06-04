using GenerativeAI;
using GenerativeAI.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using System.Threading.Channels;
using System.IO;
using System.Net;

namespace TelegramSearchBot.Service.AI.LLM
{
    [Injectable(ServiceLifetime.Transient)]
    public class GeminiService : ILLMService, IService
    {
        public string ServiceName => "GeminiService";
        private readonly ILogger<GeminiService> _logger;
        private readonly DataDbContext _dbContext;
        private readonly Dictionary<long, ChatSession> _chatSessions = new();
        private readonly IHttpClientFactory _httpClientFactory;
        public string BotName { get; set; }

        public GeminiService(
            DataDbContext context,
            ILogger<GeminiService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _dbContext = context;
            _httpClientFactory = httpClientFactory;
            _logger.LogInformation("GeminiService instance created");
        }

        private void AddMessageToHistory(List<GenerativeAI.Types.Content> chatHistory, long fromUserId, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            content = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\n{3,}", "\n\n");

            var role = fromUserId == Env.BotId ? Roles.Model : Roles.User;
            chatHistory.Add(new Content(content, role));
        }

        public async Task<List<GenerativeAI.Types.Content>> GetChatHistory(long chatId, Message inputMessage = null)
        {
            var messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == chatId && m.DateTime > DateTime.UtcNow.AddHours(-1))
                            .OrderBy(m => m.DateTime)
                            .ToListAsync();

            if (messages.Count < 10)
            {
                messages = await _dbContext.Messages.AsNoTracking()
                            .Where(m => m.GroupId == chatId)
                            .OrderByDescending(m => m.DateTime)
                            .Take(10)
                            .OrderBy(m => m.DateTime)
                            .ToListAsync();
            }

            if (inputMessage != null)
            {
                messages.Add(inputMessage);
            }

            var chatHistory = new List<GenerativeAI.Types.Content>();
            var str = new StringBuilder();
            Message previous = null;
            var userCache = new Dictionary<long, UserData>();

            foreach (var message in messages)
            {
                if (previous == null && !chatHistory.Any() && message.FromUserId == Env.BotId)
                {
                    previous = message;
                    continue;
                }

                if (previous != null && (previous.FromUserId == Env.BotId) != (message.FromUserId == Env.BotId))
                {
                    AddMessageToHistory(chatHistory, previous.FromUserId, str.ToString());
                    str.Clear();
                }

                str.Append($"[{message.DateTime:yyyy-MM-dd HH:mm:ss zzz}]");
                if (message.FromUserId != 0)
                {
                    if (!userCache.TryGetValue(message.FromUserId, out var fromUser))
                    {
                        fromUser = await _dbContext.UserData.AsNoTracking()
                            .FirstOrDefaultAsync(u => u.Id == message.FromUserId);
                        if (fromUser != null) userCache[message.FromUserId] = fromUser;
                    }
                    str.Append(fromUser != null ? $"{fromUser.FirstName} {fromUser.LastName}".Trim() : $"User({message.FromUserId})");
                }
                else
                {
                    str.Append("System/Unknown");
                }

                if (message.ReplyToMessageId != 0)
                {
                    str.Append($" (Reply to msg {message.ReplyToMessageId})");
                }
                str.Append(": ").Append(message.Content).Append("\n");

                previous = message;
            }

            if (previous != null && str.Length > 0)
            {
                AddMessageToHistory(chatHistory, previous.FromUserId, str.ToString());
            }

            return chatHistory;
        }

        public virtual async Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            if (channel.Provider.Equals(LLMProvider.Ollama)) {
                return new List<string>();
            }

            try {
                var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
                var modelsResponse = await googleAI.ListModelsAsync();
                return modelsResponse.Models.Select(m => m.Name.Replace("models/", ""));
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to list Gemini models");
                return new List<string>();
            }
        }

        /// <summary>
        /// 获取Gemini模型及其能力信息
        /// </summary>
        public virtual async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel) 
        {
            if (channel.Provider.Equals(LLMProvider.Ollama)) {
                return new List<ModelWithCapabilities>();
            }

            try {
                var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
                var modelsResponse = await googleAI.ListModelsAsync();
                var results = new List<ModelWithCapabilities>();
                
                foreach (var model in modelsResponse.Models)
                {
                    var modelName = model.Name.Replace("models/", "");
                    var modelWithCaps = InferGeminiModelCapabilities(modelName, model);
                    results.Add(modelWithCaps);
                }
                
                _logger.LogInformation("Retrieved {Count} Gemini models with capabilities", results.Count);
                return results;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to list Gemini models with capabilities");
                return new List<ModelWithCapabilities>();
            }
        }

        /// <summary>
        /// 根据Gemini模型名称和信息推断能力
        /// </summary>
        private ModelWithCapabilities InferGeminiModelCapabilities(string modelName, GenerativeAI.Types.Model modelInfo)
        {
            var model = new ModelWithCapabilities { ModelName = modelName };
            var lowerName = modelName.ToLower();
            
            // 基本能力设置
            model.SetCapability("streaming", true); // Gemini API支持流式响应
            
            // 从Gemini API模型信息中获取支持的方法
            if (modelInfo.SupportedGenerationMethods != null)
            {
                foreach (var method in modelInfo.SupportedGenerationMethods)
                {
                    if (method.ToLower().Contains("generatecontent"))
                    {
                        model.SetCapability("chat", true);
                    }
                    else if (method.ToLower().Contains("embed"))
                    {
                        model.SetCapability("embedding", true);
                    }
                }
            }
            
            // 基于模型名称推断能力
            if (lowerName.Contains("gemini"))
            {
                // Gemini模型系列能力
                model.SetCapability("function_calling", true);
                model.SetCapability("tool_calls", true);
                model.SetCapability("response_json_object", true);
                
                // Gemini 2.0和Pro模型支持更多功能
                if (lowerName.Contains("2.0") || lowerName.Contains("pro"))
                {
                    model.SetCapability("vision", true);
                    model.SetCapability("multimodal", true);
                    model.SetCapability("image_content", true);
                    model.SetCapability("audio_content", true);
                    model.SetCapability("video_content", true);
                    model.SetCapability("file_upload", true);
                }
                // Gemini 1.5系列
                else if (lowerName.Contains("1.5"))
                {
                    model.SetCapability("vision", true);
                    model.SetCapability("multimodal", true);
                    model.SetCapability("image_content", true);
                    
                    if (lowerName.Contains("pro"))
                    {
                        model.SetCapability("long_context", true);
                        model.SetCapability("file_upload", true);
                        model.SetCapability("audio_content", true);
                    }
                }
                // Flash模型 - 更快的响应
                if (lowerName.Contains("flash"))
                {
                    model.SetCapability("fast_response", true);
                    model.SetCapability("optimized", true);
                }
                
                // Pro模型 - 更强的能力
                if (lowerName.Contains("pro"))
                {
                    model.SetCapability("advanced_reasoning", true);
                    model.SetCapability("complex_tasks", true);
                }
            }
            
            // 嵌入模型检测
            if (lowerName.Contains("embedding") || lowerName.Contains("embed"))
            {
                model.SetCapability("embedding", true);
                model.SetCapability("text_embedding", true);
                model.SetCapability("function_calling", false);
                model.SetCapability("vision", false);
                model.SetCapability("chat", false);
            }
            
            // 文本生成模型
            if (lowerName.Contains("text") && !lowerName.Contains("embedding"))
            {
                model.SetCapability("text_generation", true);
                model.SetCapability("chat", true);
            }
            
            // 从模型信息中提取输入/输出token限制
            if (modelInfo.InputTokenLimit > 0)
            {
                model.SetCapability("input_token_limit", modelInfo.InputTokenLimit.ToString());
            }
            
            if (modelInfo.OutputTokenLimit > 0)
            {
                model.SetCapability("output_token_limit", modelInfo.OutputTokenLimit.ToString());
            }
            
            // 设置模型版本信息
            model.SetCapability("model_version", modelInfo.Version ?? "unknown");
            model.SetCapability("model_family", "Gemini");
            
            // 基于模型名称的特殊能力
            if (lowerName.Contains("code"))
            {
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
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(modelName)) modelName = "gemini-1.5-flash";

            var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
            var model = googleAI.CreateGenerativeModel("models/" + modelName);
            var fullResponse = new StringBuilder();

            var history = await GetChatHistory(ChatId, message);
            if (!_chatSessions.TryGetValue(ChatId, out var chatSession))
            {
                chatSession = model.StartChat(history: history);
                _chatSessions[ChatId] = chatSession;
            }

            int maxToolCycles = 5;
            var currentMessageBuilder = new StringBuilder();
            for (int cycle = 0; cycle < maxToolCycles; cycle++)
            {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();

                var fullResponseBuilder = new StringBuilder();

                await foreach (var chunk in chatSession.StreamContentAsync(message.Content)) 
                {
                    currentMessageBuilder.Append(chunk.Text);
                    fullResponseBuilder.Append(chunk.Text);
                    yield return currentMessageBuilder.ToString();
                }

                string llmResponse = fullResponseBuilder.ToString().Trim();
                _logger.LogDebug("Gemini raw response (Cycle {Cycle}): {Response}", cycle + 1, llmResponse);

                if (McpToolHelper.TryParseToolCalls(llmResponse, out var toolCalls) && toolCalls.Any())
                {
                    var firstToolCall = toolCalls[0];
                    _logger.LogInformation("Gemini requested tool: {ToolName} with args: {Args}", 
                        firstToolCall.toolName, 
                        JsonConvert.SerializeObject(firstToolCall.arguments));

                    string toolResult;
                    bool isError = false;
                    try
                    {
                        var toolContext = new ToolContext { ChatId = ChatId };
                        var result = await McpToolHelper.ExecuteRegisteredToolAsync(
                            firstToolCall.toolName, 
                            firstToolCall.arguments, 
                            toolContext);
                        toolResult = McpToolHelper.ConvertToolResultToString(result);
                    }
                    catch (Exception ex)
                    {
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

            yield return "Maximum tool call cycles reached. Please try again.";
        }

        public async Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel)
        {
            if (channel == null || string.IsNullOrWhiteSpace(channel.ApiKey))
            {
                _logger.LogError("{ServiceName}: Channel or ApiKey is not configured", ServiceName);
                throw new ArgumentException("Channel or ApiKey is not configured");
            }

            try
            {
                var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
                var embeddings = googleAI.CreateEmbeddingModel("models/embedding-001");
                var response = await embeddings.EmbedContentAsync(text);
#pragma warning disable CS8602 // 解引用可能出现空引用。
                return response.Embedding.Values.Select(v => (float)v).ToArray();
#pragma warning restore CS8602 // 解引用可能出现空引用。
            }
            catch (Exception ex)
            {
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

        private GenerativeModel GetGenerativeModel(LLMRequest request)
        {
            var apiKey = request.Channel?.ApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentException("API key is missing in LLMChannelDto for GeminiService.");
            }

            var clientHandler = new HttpClientHandler();
            if (request.Channel?.ExtendedConfig != null &&
                request.Channel.ExtendedConfig.TryGetValue("ProxyUrl", out var proxyUrlObj) &&
                proxyUrlObj is string proxyUrl && !string.IsNullOrWhiteSpace(proxyUrl))
            {
                clientHandler.Proxy = new WebProxy(proxyUrl);
                clientHandler.UseProxy = true;
                _logger.LogInformation("GeminiService using proxy: {ProxyUrl}", proxyUrl);
            }
            else
            {
                // Use system proxy settings if no explicit proxy is defined
                clientHandler.UseProxy = true; 
                clientHandler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
            }

            var httpClient = _httpClientFactory.CreateClient("GeminiClient"); // Use a named client if configured, or default
            // If not using named client from factory that already has handler, create new HttpClient with handler:
            // var httpClient = new HttpClient(clientHandler); 
            // For now, assuming _httpClientFactory.CreateClient() gives a client we can work with or we create one.
            // The GoogleAi constructor takes an optional HttpClient. We should ensure it uses our handler.
            // A common pattern is: var httpClient = new HttpClient(clientHandler, disposeHandler: false); and then pass it.
            // Let's re-evaluate how GoogleAi takes the client and handler.
            // The GoogleAi constructor directly takes an HttpClient. So we construct one with our handler.
            httpClient = new HttpClient(clientHandler, disposeHandler: true); // Dispose handler when client is disposed


            var googleAI = new GoogleAi(apiKey, client: httpClient); 
            
            string modelName = request.Model;
            // The Gemini SDK might expect "models/gemini-pro" or just "gemini-pro".
            // The official docs often show "gemini-1.5-flash-latest" or "models/gemini-1.0-pro"
            // Let's assume the request.Model is the part after "models/" or the full name.
            // The SDK internally prefixes with "models/" if not present for some calls, but explicit is safer.
            if (!modelName.Contains("/")) // if it's a simple name like "gemini-pro"
            {
                 // Check if it's one of the known short names that don't need models/ prefix for this SDK version
                 // For GetModel, it seems it wants the full path e.g. "models/gemini-pro"
                 if(!modelName.StartsWith("models/")) modelName = $"models/{modelName}";
            }
            // if request.Model is already like "models/gemini-pro", it will be used as is.

            return googleAI.GetModel(modelName);
        }

        private GenerationConfig ConvertGenerationOptions(LLMGenerationOptions options)
        {
            if (options == null) return new GenerationConfig(); // Return default if null
            return new GenerationConfig
            {
                Temperature = (float?)options.Temperature,
                TopP = (float?)options.TopP,
                TopK = options.TopK,
                MaxOutputTokens = options.MaxTokens,
                StopSequences = options.StopSequences?.ToList() // Ensure it's a List
            };
        }

        private List<Part> ConvertLLMContentToParts(TelegramSearchBot.Model.AI.LLMContent c, long requestIdForLog)
        {
            var parts = new List<Part>();
            if (c == null) return parts;

            if (c.Type == TelegramSearchBot.Model.AI.LLMContentType.Text && !string.IsNullOrEmpty(c.Text))
            {
                parts.Add(new Part { Text = c.Text });
            }
            else if (c.Type == TelegramSearchBot.Model.AI.LLMContentType.Image && c.Image != null)
            {
                if (!string.IsNullOrEmpty(c.Image.Data)) // Prefer base64 data
                {
                    parts.Add(new Part
                    {
                        InlineData = new GenerativeAI.Types.Blob
                        {
                            MimeType = !string.IsNullOrEmpty(c.Image.MimeType) ? c.Image.MimeType : "image/jpeg", // Default MIME type
                            Data = c.Image.Data
                        }
                    });
                }
                else if (!string.IsNullOrEmpty(c.Image.Url))
                {
                    _logger.LogWarning("RequestId: {RequestId} - GeminiService: Image content with URL only is not directly supported for InlineData. Image URL: {ImageUrl}. Description: {Description}", requestIdForLog, c.Image.Url, c.Image.Description);
                    // As a fallback, if there's a description, use that as text. Otherwise, a placeholder.
                    string fallbackText = !string.IsNullOrWhiteSpace(c.Image.Description) ? $"[Image Description: {c.Image.Description}]" : $"[Image at {c.Image.Url} - URL not processed]";
                    parts.Add(new Part { Text = fallbackText });
                }
            }
            // TODO: Add handling for other types like Audio, File if Gemini SDK supports them in 'Part' and if old LLMContent supports them.
            // Old LLMContent has Audio (LLMAudioContent) and File (LLMFileContent).
            // Gemini Part can also contain FunctionCall and FunctionResponse, but that's for tool usage flow.
            // For now, only Text and Image are handled from LLMContent.
            return parts;
        }

        private List<GenerativeAI.Types.Content> ConvertChatHistory(
            List<TelegramSearchBot.Model.AI.LLMMessage> history,
            TelegramSearchBot.Model.AI.LLMMessage currentMessage, 
            string requestIdForLog)
        {
            var geminiContents = new List<GenerativeAI.Types.Content>();
            var allMessages = new List<TelegramSearchBot.Model.AI.LLMMessage>(history ?? new List<TelegramSearchBot.Model.AI.LLMMessage>());
            if (currentMessage != null)
            {
                allMessages.Add(currentMessage);
            }

            foreach (var msg in allMessages.Where(m => m != null))
            {
                // Map roles. Gemini uses "user" and "model". System prompts are handled separately.
                // Tool role messages are also specific and usually part of a tool calling flow.
                string role;
                if (msg.Role == TelegramSearchBot.Model.AI.LLMRole.User)
                {
                    role = Roles.User;
                }
                else if (msg.Role == TelegramSearchBot.Model.AI.LLMRole.Assistant)
                {
                    role = Roles.Model;
                }
                else
                {
                     _logger.LogWarning("RequestId: {RequestId} - GeminiService: Unsupported role {UnsupportedRole} in chat history, skipping message: {MessageContent}", requestIdForLog, msg.Role, msg.Content?.Substring(0, Math.Min(msg.Content?.Length ?? 0, 50)));
                    continue; // Skip system/tool messages in history for now, or map to user/model if appropriate.
                }
                
                var messageParts = new List<Part>();

                // Add primary content if it exists and is not empty
                if (!string.IsNullOrWhiteSpace(msg.Content))
                {
                    messageParts.Add(new Part { Text = msg.Content });
                }

                // Add parts from multimodal Contents if they exist
                if (msg.Contents != null)
                {
                    foreach (var contentItem in msg.Contents.Where(ci => ci != null))
                    {
                        messageParts.AddRange(ConvertLLMContentToParts(contentItem, requestIdForLog));
                    }
                }
                
                if (!messageParts.Any())
                {
                    // If a message (e.g. assistant message) after processing has no parts (e.g. empty content and no supported multimodal parts), skip it.
                    // Or, if it must be preserved, add a placeholder like new Part { Text = "[empty message]")
                    _logger.LogWarning("RequestId: {RequestId} - GeminiService: Message from role {Role} resulted in no parts, skipping.", requestIdForLog, role);
                    continue;
                }

                // Gemini expects alternating user/model roles. Coalesce if necessary, though ideally history is already structured.
                // For now, assume history is mostly fine, but be mindful of Gemini API strictness.
                // If the last content has the same role, merge parts. This is crucial for Gemini.
                if (geminiContents.Any() && geminiContents.Last().Role == role)
                {
                    geminiContents.Last().Parts.AddRange(messageParts);
                }
                else
                {
                    geminiContents.Add(new GenerativeAI.Types.Content { Role = role, Parts = messageParts });
                }
            }
            return geminiContents;
        }
        
        private GenerativeAI.Types.Content ConvertSystemPrompt(string systemPromptText)
        {
            if (string.IsNullOrWhiteSpace(systemPromptText))
            {
                return null;
            }
            return new GenerativeAI.Types.Content 
            { 
                Role = "system", 
                Parts = new[] { new Part { Text = systemPromptText } } 
            };
        }

        public async Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("GeminiService ExecuteAsync started. RequestId: {RequestId}, Model: {Model}", request.RequestId, request.Model);

            try
            {
                var generativeModel = GetGenerativeModel(request);
                var geminiChatHistory = ConvertChatHistory(request.ChatHistory, request.CurrentMessage, request.RequestId);
                var generationConfig = ConvertGenerationOptions(request.Options);
                var systemInstruction = ConvertSystemPrompt(request.SystemPrompt);

                // 如果有系统指令，将其添加到对话历史的开头
                var allContents = new List<GenerativeAI.Types.Content>();
                if (systemInstruction != null)
                {
                    allContents.Add(systemInstruction);
                }
                allContents.AddRange(geminiChatHistory);

                var genRequest = new GenerateContentRequest
                {
                    Contents = allContents.ToArray(),
                    GenerationConfig = generationConfig,
                    // TODO: Convert and add Tools if request.EnableTools and request.AvailableTools are present
                };

                _logger.LogDebug("GeminiService ExecuteAsync - Request to Gemini: {GeminiRequest}", JsonConvert.SerializeObject(genRequest, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

                var response = await generativeModel.GenerateContentAsync(genRequest, cancellationToken);

                _logger.LogDebug("GeminiService ExecuteAsync - Response from Gemini: {GeminiResponse}", JsonConvert.SerializeObject(response, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

                string responseText = response?.GetText();
                // TODO: Extract TokenUsage if available from response.UsageMetadata

                if (response == null || string.IsNullOrEmpty(responseText))
                {
                    _logger.LogWarning("GeminiService ExecuteAsync received null or empty response text for RequestId: {RequestId}", request.RequestId);
                    return new LLMResponse
                    {
                        RequestId = request.RequestId,
                        Model = request.Model,
                        IsSuccess = false,
                        ErrorMessage = "Gemini returned no content or empty text.",
                        StartTime = startTime,
                        EndTime = DateTime.UtcNow
                    };
                }

                return new LLMResponse
                {
                    RequestId = request.RequestId,
                    Model = request.Model,
                    IsSuccess = true,
                    Content = responseText,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    TokenUsage = new LLMTokenUsage() // Placeholder
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GeminiService ExecuteAsync failed for RequestId: {RequestId}. Error: {ErrorMessage}", request.RequestId, ex.Message);
                return new LLMResponse
                {
                    RequestId = request.RequestId,
                    Model = request.Model,
                    IsSuccess = false,
                    ErrorMessage = ex.ToString(), // Include full exception details for better debugging
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow
                };
            }
        }

        public Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("GeminiService ExecuteStreamAsync started. RequestId: {RequestId}, Model: {Model}", request.RequestId, request.Model);

            var channel = System.Threading.Channels.Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

            var responseBuildingTask = Task.Run(async () =>
            {
                var fullResponseText = new StringBuilder();
                try
                {
                    var generativeModel = GetGenerativeModel(request);
                    var geminiChatHistory = ConvertChatHistory(request.ChatHistory, request.CurrentMessage, request.RequestId);
                    var generationConfig = ConvertGenerationOptions(request.Options);
                    var systemInstruction = ConvertSystemPrompt(request.SystemPrompt);

                    // 如果有系统指令，将其添加到对话历史的开头
                    var allContents = new List<GenerativeAI.Types.Content>();
                    if (systemInstruction != null)
                    {
                        allContents.Add(systemInstruction);
                    }
                    allContents.AddRange(geminiChatHistory);

                    var genRequest = new GenerateContentRequest
                    {
                        Contents = allContents.ToArray(),
                        GenerationConfig = generationConfig,
                        // TODO: Convert and add Tools for streaming if supported/needed
                    };

                    _logger.LogDebug("GeminiService ExecuteStreamAsync - Request to Gemini: {GeminiRequest}", JsonConvert.SerializeObject(genRequest, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

                    var stream = generativeModel.GenerateContentStreamAsync(genRequest, cancellationToken);
                    
                    await foreach (var responseChunk in stream.WithCancellation(cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested) 
                        {
                            _logger.LogInformation("GeminiService ExecuteStreamAsync cancellation requested for RequestId: {RequestId}", request.RequestId);
                            break;
                        }
                        
                        string chunkText = responseChunk?.GetText();
                        if (!string.IsNullOrEmpty(chunkText))
                        {
                            fullResponseText.Append(chunkText);
                            await channel.Writer.WriteAsync(chunkText, cancellationToken);
                        }
                        // TODO: Aggregate TokenUsage from chunks if available (responseChunk.UsageMetadata?)
                    }
                    
                    channel.Writer.TryComplete();
                    _logger.LogInformation("GeminiService ExecuteStreamAsync completed successfully for RequestId: {RequestId}", request.RequestId);
                    return new LLMResponse
                    {
                        RequestId = request.RequestId,
                        Model = request.Model,
                        IsSuccess = true,
                        Content = fullResponseText.ToString(),
                        StartTime = startTime,
                        EndTime = DateTime.UtcNow,
                        IsStreaming = true,
                        TokenUsage = new LLMTokenUsage() // Placeholder
                    };
                }
                catch (OperationCanceledException opEx) when (opEx.CancellationToken == cancellationToken)
                {
                    _logger.LogWarning(opEx, "GeminiService ExecuteStreamAsync was canceled for RequestId: {RequestId}", request.RequestId);
                    channel.Writer.TryComplete(opEx);
                    return new LLMResponse
                    {
                        RequestId = request.RequestId,
                        Model = request.Model,
                        IsSuccess = false,
                        ErrorMessage = "Streaming was canceled.",
                        StartTime = startTime,
                        EndTime = DateTime.UtcNow,
                        IsStreaming = true
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GeminiService ExecuteStreamAsync failed during streaming for RequestId: {RequestId}. Error: {ErrorMessage}", request.RequestId, ex.Message);
                    channel.Writer.TryComplete(ex);
                    return new LLMResponse
                    {
                        RequestId = request.RequestId,
                        Model = request.Model,
                        IsSuccess = false,
                        ErrorMessage = ex.ToString(), // Full exception details
                        StartTime = startTime,
                        EndTime = DateTime.UtcNow,
                        IsStreaming = true
                    };
                }
            }, cancellationToken);

            return Task.FromResult((channel.Reader, responseBuildingTask));
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text, string model, LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            // Gemini当前不支持嵌入向量生成
            throw new NotSupportedException("Gemini服务暂不支持嵌入向量生成");
        }

        public Task<List<string>> GetAvailableModelsAsync(LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            // 返回Gemini支持的模型列表
            var models = new List<string> { "gemini-pro", "gemini-pro-vision" };
            return Task.FromResult(models);
        }

        public Task<bool> IsHealthyAsync(LLMChannelDto channel, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        /// <summary>
        /// 检查Gemini服务健康状况 (LLMChannel重载版本)
        /// </summary>
        public async Task<bool> IsHealthyAsync(LLMChannel channel)
        {
            try
            {
                var googleAI = new GoogleAi(channel.ApiKey, client: _httpClientFactory.CreateClient());
                var modelsResponse = await googleAI.ListModelsAsync();
                return modelsResponse?.Models?.Any() == true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini健康检查失败: {Error}", ex.Message);
                return false;
            }
        }
    }
}

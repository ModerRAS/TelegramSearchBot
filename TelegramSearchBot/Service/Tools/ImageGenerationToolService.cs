using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Tools;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(ServiceLifetime.Transient)]
    public class ImageGenerationToolSettingsService : IService {
        public const string EnableToolKey = "LLM:EnableImageGenerationTool";
        public const string ModelNameKey = "LLM:ImageGenerationModelName";
        public const string DefaultModelName = "gpt-image-2";

        public string ServiceName => "ImageGenerationToolSettingsService";

        private readonly DataDbContext _dbContext;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly ILogger<ImageGenerationToolSettingsService> _logger;

        public ImageGenerationToolSettingsService(
            DataDbContext dbContext,
            IConnectionMultiplexer connectionMultiplexer,
            ILogger<ImageGenerationToolSettingsService> logger) {
            _dbContext = dbContext;
            _connectionMultiplexer = connectionMultiplexer;
            _logger = logger;
        }

        public async Task InitializeToolVisibilityAsync() {
            var enabled = await IsToolEnabledAsync();
            ApplyToolVisibility(enabled);
        }

        public async Task<bool> IsToolEnabledAsync() {
            var value = await GetConfigurationValueAsync(EnableToolKey);
            return ParseBool(value, defaultValue: true);
        }

        public async Task SetToolEnabledAsync(bool enabled) {
            await UpsertConfigurationAsync(EnableToolKey, enabled ? "true" : "false");
            ApplyToolVisibility(enabled);
            await RefreshAgentToolDefinitionsAsync();
        }

        public async Task<string> GetDefaultModelNameAsync() {
            var value = await GetConfigurationValueAsync(ModelNameKey);
            return string.IsNullOrWhiteSpace(value) ? DefaultModelName : value.Trim();
        }

        public Task<string> GetModelNameAsync() {
            return GetDefaultModelNameAsync();
        }

        public async Task<string> GetModelNameAsync(long chatId) {
            if (chatId != 0) {
                var groupModelName = await _dbContext.GroupSettings
                    .AsNoTracking()
                    .Where(x => x.GroupId == chatId)
                    .Select(x => x.ImageGenerationModelName)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(groupModelName)) {
                    return groupModelName.Trim();
                }
            }

            return await GetDefaultModelNameAsync();
        }

        public async Task SetDefaultModelNameAsync(string modelName) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                throw new ArgumentException("Image generation model name cannot be empty.", nameof(modelName));
            }

            await UpsertConfigurationAsync(ModelNameKey, modelName.Trim());
        }

        public Task SetModelNameAsync(string modelName) {
            return SetDefaultModelNameAsync(modelName);
        }

        public async Task<(string Previous, string Current)> SetGroupModelNameAsync(long chatId, string modelName) {
            if (chatId == 0) {
                throw new ArgumentException("Chat id is required.", nameof(chatId));
            }

            if (string.IsNullOrWhiteSpace(modelName)) {
                throw new ArgumentException("Image generation model name cannot be empty.", nameof(modelName));
            }

            var normalizedModelName = modelName.Trim();
            var defaultModelName = await GetDefaultModelNameAsync();
            var settings = await _dbContext.GroupSettings
                .FirstOrDefaultAsync(x => x.GroupId == chatId);
            var previous = settings == null ? null : settings.ImageGenerationModelName;
            GroupSettings newSettings = null;

            if (settings == null) {
                newSettings = new GroupSettings {
                    GroupId = chatId,
                    ImageGenerationModelName = normalizedModelName
                };
                await _dbContext.GroupSettings.AddAsync(newSettings);
            } else {
                settings.ImageGenerationModelName = normalizedModelName;
            }

            try {
                await _dbContext.SaveChangesAsync();
            } catch (DbUpdateException) when (newSettings != null) {
                _dbContext.Entry(newSettings).State = EntityState.Detached;
                settings = await _dbContext.GroupSettings
                    .FirstOrDefaultAsync(x => x.GroupId == chatId);
                if (settings == null) {
                    throw;
                }

                previous = settings.ImageGenerationModelName;
                settings.ImageGenerationModelName = normalizedModelName;
                await _dbContext.SaveChangesAsync();
            }

            return (string.IsNullOrWhiteSpace(previous) ? defaultModelName : previous.Trim(), normalizedModelName);
        }

        public async Task<string> ClearGroupModelNameAsync(long chatId) {
            if (chatId == 0) {
                throw new ArgumentException("Chat id is required.", nameof(chatId));
            }

            var settings = await _dbContext.GroupSettings
                .FirstOrDefaultAsync(x => x.GroupId == chatId);
            if (settings != null) {
                settings.ImageGenerationModelName = null;
                await _dbContext.SaveChangesAsync();
            }

            return await GetDefaultModelNameAsync();
        }

        private static void ApplyToolVisibility(bool enabled) {
            McpToolHelper.SetBuiltInToolEnabled(ImageGenerationToolService.ToolName, enabled);
        }

        private async Task RefreshAgentToolDefinitionsAsync() {
            try {
                await McpToolHelper.RefreshAgentToolDefsInRedisAsync(_connectionMultiplexer);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to refresh agent tool definitions after image generation tool visibility changed.");
            }
        }

        private async Task<string> GetConfigurationValueAsync(string key) {
            var item = await _dbContext.AppConfigurationItems
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == key);
            return item?.Value;
        }

        private async Task UpsertConfigurationAsync(string key, string value) {
            var item = await _dbContext.AppConfigurationItems
                .FirstOrDefaultAsync(x => x.Key == key);

            if (item == null) {
                await _dbContext.AppConfigurationItems.AddAsync(new AppConfigurationItem {
                    Key = key,
                    Value = value
                });
            } else {
                item.Value = value;
            }

            await _dbContext.SaveChangesAsync();
        }

        private static bool ParseBool(string value, bool defaultValue) {
            if (string.IsNullOrWhiteSpace(value)) {
                return defaultValue;
            }

            if (bool.TryParse(value.Trim(), out var parsed)) {
                return parsed;
            }

            return value.Trim() switch {
                "1" or "on" or "yes" or "enable" or "enabled" => true,
                "0" or "off" or "no" or "disable" or "disabled" => false,
                _ => defaultValue
            };
        }
    }

    [Injectable(ServiceLifetime.Transient)]
    public class ImageGenerationToolService : IService {
        public const string ToolName = "generate_image";
        private const string DefaultSize = "1024x1024";
        private const string DefaultQuality = "auto";
        private const string DefaultOutputFormat = "png";
        private const int MaxImageCount = 9;
        private const string DefaultMiniMaxResponseFormat = "base64";
        private const long TelegramPhotoLimitBytes = 10 * 1024 * 1024;
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(120);
        private static readonly string[] MiniMaxImageModels = { "image-01", "image-01-live" };
        private static readonly string[] MiniMaxAspectRatios = { "1:1", "16:9", "4:3", "3:2", "2:3", "3:4", "9:16", "21:9" };
        private static readonly string[] MiniMaxStyleTypes = { "漫画", "元气", "中世纪", "水彩" };

        public string ServiceName => "ImageGenerationToolService";

        private readonly DataDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly ImageGenerationToolSettingsService _settingsService;
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<ImageGenerationToolService> _logger;

        public ImageGenerationToolService(
            DataDbContext dbContext,
            IHttpClientFactory httpClientFactory,
            IConnectionMultiplexer connectionMultiplexer,
            ImageGenerationToolSettingsService settingsService,
            ITelegramBotClient botClient,
            SendMessage sendMessage,
            ILogger<ImageGenerationToolService> logger) {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
            _connectionMultiplexer = connectionMultiplexer;
            _settingsService = settingsService;
            _botClient = botClient;
            _sendMessage = sendMessage;
            _logger = logger;
        }

        [BuiltInTool(@"Generate an image through the configured image API and optionally send it to the current Telegram chat.
The default model is the current chat's configured image generation model, falling back to the bot-wide default gpt-image-2 when the chat has no image model configured. OpenAI-compatible models use /v1/images/generations. MiniMax image-01 and image-01-live use /v1/image_generation. The API base URL and API key are read from the configured LLM channel for the selected image model, so administrators can use OpenAI, MiniMax, or a compatible custom endpoint.
Use this when the user asks you to draw, create, render, generate, or revise an image. The tool saves generated image files under the bot work directory and returns their file paths.")]
        public async Task<ImageGenerationResult> GenerateImage(
            [BuiltInParameter("Image prompt. Be specific about subject, style, composition, lighting, colors, and any text that should appear.")] string prompt,
            ToolContext toolContext,
            [BuiltInParameter("Image model name. Defaults to the current chat's configured image generation model, or the bot-wide default gpt-image-2 when unset. Use image-01 or image-01-live for MiniMax.", IsRequired = false)] string model = null,
            [BuiltInParameter("Image size such as 1024x1024, 1536x1024, 1024x1536, or auto. Defaults to 1024x1024.", IsRequired = false)] string size = DefaultSize,
            [BuiltInParameter("Quality: auto, low, medium, or high. Defaults to auto.", IsRequired = false)] string quality = DefaultQuality,
            [BuiltInParameter("OpenAI-compatible output format: png, jpeg, or webp. Defaults to png. MiniMax does not support choosing output file format; MiniMax base64 results are saved as png unless a downloaded URL provides a more specific content type.", IsRequired = false)] string outputFormat = DefaultOutputFormat,
            [BuiltInParameter("Background: auto, opaque, or transparent. Leave empty unless the user asked for a specific background.", IsRequired = false)] string background = null,
            [BuiltInParameter("Moderation setting: auto or low. Defaults to auto.", IsRequired = false)] string moderation = "auto",
            [BuiltInParameter("MiniMax aspect ratio: 1:1, 16:9, 4:3, 3:2, 2:3, 3:4, 9:16, or 21:9. If set, MiniMax ignores size. Leave empty for OpenAI-compatible models.", IsRequired = false)] string aspectRatio = null,
            [BuiltInParameter("MiniMax response format: base64 or url. Defaults to base64. Leave empty for OpenAI-compatible models.", IsRequired = false)] string minimaxResponseFormat = DefaultMiniMaxResponseFormat,
            [BuiltInParameter("MiniMax random seed for reproducible similar results. Leave empty unless the user asks for a seed.", IsRequired = false)] long? seed = null,
            [BuiltInParameter("Whether to enable MiniMax prompt_optimizer. Defaults to false.", IsRequired = false)] bool promptOptimizer = false,
            [BuiltInParameter("Whether to add the MiniMax AIGC watermark. Defaults to false.", IsRequired = false)] bool aigcWatermark = false,
            [BuiltInParameter("MiniMax image-01-live style_type. Supported values: 漫画, 元气, 中世纪, 水彩. Leave empty unless the user asks for one of these styles.", IsRequired = false)] string styleType = null,
            [BuiltInParameter("MiniMax image-01-live style_weight in (0, 1]. Defaults to MiniMax service default when empty.", IsRequired = false)] double? styleWeight = null,
            [BuiltInParameter("Number of images to generate, from 1 to 9. Defaults to 1.", IsRequired = false)] int count = 1,
            [BuiltInParameter("Whether to send generated images to the current Telegram chat. Defaults to true.", IsRequired = false)] bool sendToChat = true,
            [BuiltInParameter("Optional Telegram photo caption. Keep it short.", IsRequired = false)] string caption = null,
            [BuiltInParameter("Optional Telegram message ID to reply to. Defaults to the original user message.", IsRequired = false)] long? replyToMessageId = null,
            [BuiltInParameter("Request timeout in seconds, from 30 to 600. Defaults to 300.", IsRequired = false)] int timeoutSeconds = 300) {
            var result = new ImageGenerationResult();

            try {
                if (!await _settingsService.IsToolEnabledAsync()) {
                    return Failure("Image generation tool is disabled by the administrator.");
                }

                if (string.IsNullOrWhiteSpace(prompt)) {
                    return Failure("Prompt cannot be empty.");
                }

                var effectiveModel = string.IsNullOrWhiteSpace(model)
                    ? await _settingsService.GetModelNameAsync(toolContext?.ChatId ?? 0)
                    : model.Trim();
                var normalizedSize = NormalizeSize(size);
                var normalizedQuality = NormalizeChoice(quality, "quality", DefaultQuality, "auto", "low", "medium", "high");
                var normalizedOutputFormat = NormalizeChoice(outputFormat, "outputFormat", DefaultOutputFormat, "png", "jpeg", "webp");
                var normalizedBackground = NormalizeOptionalChoice(background, "background", "auto", "opaque", "transparent");
                var normalizedModeration = NormalizeOptionalChoice(moderation, "moderation", "auto", "low") ?? "auto";
                var normalizedMiniMaxResponseFormat = string.IsNullOrWhiteSpace(minimaxResponseFormat)
                    ? DefaultMiniMaxResponseFormat
                    : minimaxResponseFormat.Trim();
                var normalizedCount = Math.Clamp(count, 1, MaxImageCount);
                var normalizedTimeoutSeconds = Math.Clamp(timeoutSeconds, 30, 600);

                result.Model = effectiveModel;

                var channels = await LoadChannelsAsync(effectiveModel);
                if (channels.Count == 0) {
                    return Failure(
                        $"No channel is configured for image model '{effectiveModel}'. Add an OpenAI-compatible or MiniMax channel and associate model '{effectiveModel}' with it. The channel gateway can be a custom base URL, for example https://api.openai.com/v1 or https://api.minimaxi.com.");
                }

                Exception lastException = null;
                foreach (var channel in channels) {
                    if (!await TryAcquireChannelAsync(channel)) {
                        continue;
                    }

                    try {
                        var useMiniMax = IsMiniMaxImageGeneration(channel, effectiveModel);
                        var endpoint = useMiniMax
                            ? BuildMiniMaxImageGenerationEndpoint(channel.Gateway)
                            : BuildImageGenerationEndpoint(channel.Gateway);
                        var generated = useMiniMax
                            ? await RequestMiniMaxImagesAsync(
                                endpoint,
                                channel,
                                prompt.Trim(),
                                effectiveModel,
                                normalizedSize,
                                DefaultOutputFormat,
                                normalizedMiniMaxResponseFormat,
                                aspectRatio,
                                seed,
                                promptOptimizer,
                                aigcWatermark,
                                styleType,
                                styleWeight,
                                normalizedCount,
                                normalizedTimeoutSeconds)
                            : await RequestImagesAsync(
                                endpoint,
                                channel,
                                prompt.Trim(),
                                effectiveModel,
                                normalizedSize,
                                normalizedQuality,
                                normalizedOutputFormat,
                                normalizedBackground,
                                normalizedModeration,
                                normalizedCount,
                                normalizedTimeoutSeconds);

                        result.ChannelId = channel.Id;
                        result.ChannelName = channel.Name;
                        result.Endpoint = endpoint;
                        result.Images = generated.Select(x => x.Info).ToList();

                        if (sendToChat) {
                            foreach (var image in generated) {
                                result.SentPhotos.Add(await SendGeneratedImageAsync(
                                    image.Bytes,
                                    Path.GetFileName(image.Info.FilePath),
                                    toolContext,
                                    caption,
                                    replyToMessageId));
                            }
                        }

                        result.Success = result.Images.Count > 0;
                        if (result.Success && result.SentPhotos.Any(x => !x.Success)) {
                            result.Error = "Generated image files were saved, but one or more Telegram photo sends failed.";
                        }

                        return result;
                    } catch (Exception ex) {
                        lastException = ex;
                        _logger.LogWarning(ex, "Image generation failed for channel {ChannelId} ({ChannelName}) and model {Model}.", channel.Id, channel.Name, effectiveModel);
                    } finally {
                        await ReleaseChannelAsync(channel);
                    }
                }

                return Failure(lastException == null
                    ? $"All channels for image model '{effectiveModel}' are currently at capacity."
                    : $"Image generation failed: {lastException.Message}");
            } catch (Exception ex) {
                _logger.LogError(ex, "Image generation tool failed.");
                return Failure($"Image generation failed: {ex.Message}");
            }

            ImageGenerationResult Failure(string error) {
                return new ImageGenerationResult {
                    Success = false,
                    Error = error,
                    Model = result.Model
                };
            }
        }

        public static string BuildImageGenerationEndpoint(string gateway) {
            if (string.IsNullOrWhiteSpace(gateway)) {
                throw new ArgumentException("Image generation channel gateway cannot be empty.", nameof(gateway));
            }

            var normalized = gateway.Trim().TrimEnd('/');
            if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
                return $"{normalized}/images/generations";
            }

            if (normalized.EndsWith("/images/generations", StringComparison.OrdinalIgnoreCase)) {
                return normalized;
            }

            return $"{normalized}/v1/images/generations";
        }

        public static string BuildMiniMaxImageGenerationEndpoint(string gateway) {
            if (string.IsNullOrWhiteSpace(gateway)) {
                throw new ArgumentException("MiniMax image generation channel gateway cannot be empty.", nameof(gateway));
            }

            var normalized = gateway.Trim().TrimEnd('/');
            if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
                return $"{normalized}/image_generation";
            }

            if (normalized.EndsWith("/v1/image_generation", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith("/image_generation", StringComparison.OrdinalIgnoreCase)) {
                return normalized;
            }

            return $"{normalized}/v1/image_generation";
        }

        private async Task<List<LLMChannel>> LoadChannelsAsync(string modelName) {
            var channelIds = await _dbContext.ChannelsWithModel
                .AsNoTracking()
                .Where(x => x.ModelName == modelName && !x.IsDeleted)
                .Select(x => x.LLMChannelId)
                .Distinct()
                .ToListAsync();

            if (channelIds.Count == 0) {
                return new List<LLMChannel>();
            }

            return await _dbContext.LLMChannels
                .AsNoTracking()
                .Where(x => channelIds.Contains(x.Id))
                .OrderByDescending(x => x.Priority)
                .ToListAsync();
        }

        private async Task<bool> TryAcquireChannelAsync(LLMChannel channel) {
            var redisDb = _connectionMultiplexer.GetDatabase();
            var key = GetSemaphoreKey(channel.Id);
            var currentCount = await redisDb.StringGetAsync(key);
            var current = currentCount.HasValue ? ( int ) currentCount : 0;
            var limit = Math.Max(1, channel.Parallel);
            if (current >= limit) {
                return false;
            }

            await redisDb.StringIncrementAsync(key);
            return true;
        }

        private async Task ReleaseChannelAsync(LLMChannel channel) {
            try {
                await _connectionMultiplexer.GetDatabase().StringDecrementAsync(GetSemaphoreKey(channel.Id));
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to release image generation semaphore for channel {ChannelId}.", channel.Id);
            }
        }

        private static string GetSemaphoreKey(int channelId) => $"llm:channel:{channelId}:image_generation_semaphore";

        private async Task<List<GeneratedImagePayload>> RequestImagesAsync(
            string endpoint,
            LLMChannel channel,
            string prompt,
            string model,
            string size,
            string quality,
            string outputFormat,
            string background,
            string moderation,
            int count,
            int timeoutSeconds) {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var requestBody = new JObject {
                ["model"] = model,
                ["prompt"] = prompt,
                ["size"] = size,
                ["quality"] = quality,
                ["n"] = count,
                ["output_format"] = outputFormat
            };

            if (!string.IsNullOrWhiteSpace(background)) {
                requestBody["background"] = background;
            }

            if (!string.IsNullOrWhiteSpace(moderation)) {
                requestBody["moderation"] = moderation;
            }

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = Timeout.InfiniteTimeSpan;

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) {
                Content = new StringContent(requestBody.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(channel.ApiKey)) {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {channel.ApiKey}");
            }

            using var response = await httpClient.SendAsync(request, cts.Token);
            var responseText = await response.Content.ReadAsStringAsync(cts.Token);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"Image API returned HTTP {( int ) response.StatusCode}: {ExtractErrorMessage(responseText)}");
            }

            var images = ExtractImages(responseText);
            if (images.Count == 0) {
                throw new InvalidOperationException("The image API response did not include b64_json or url image data.");
            }

            var saved = new List<GeneratedImagePayload>();
            for (var i = 0; i < images.Count; i++) {
                var savedImage = await SaveImageAsync(httpClient, images[i], outputFormat, i + 1, cts.Token);
                saved.Add(savedImage);
            }

            return saved;
        }

        private async Task<List<GeneratedImagePayload>> RequestMiniMaxImagesAsync(
            string endpoint,
            LLMChannel channel,
            string prompt,
            string model,
            string size,
            string outputFormat,
            string responseFormat,
            string aspectRatio,
            long? seed,
            bool promptOptimizer,
            bool aigcWatermark,
            string styleType,
            double? styleWeight,
            int count,
            int timeoutSeconds) {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var requestBody = BuildMiniMaxImageGenerationRequestBody(
                prompt,
                model,
                size,
                count,
                responseFormat,
                aspectRatio,
                seed,
                promptOptimizer,
                aigcWatermark,
                styleType,
                styleWeight);

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = Timeout.InfiniteTimeSpan;

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) {
                Content = new StringContent(requestBody.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(channel.ApiKey)) {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {channel.ApiKey}");
            }

            using var response = await httpClient.SendAsync(request, cts.Token);
            var responseText = await response.Content.ReadAsStringAsync(cts.Token);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"MiniMax image API returned HTTP {( int ) response.StatusCode}: {ExtractErrorMessage(responseText)}");
            }

            EnsureMiniMaxResponseSucceeded(responseText);
            var images = ExtractImages(responseText);
            if (images.Count == 0) {
                throw new InvalidOperationException("The MiniMax image API response did not include image_urls or image_base64 data.");
            }

            var saved = new List<GeneratedImagePayload>();
            for (var i = 0; i < images.Count; i++) {
                var savedImage = await SaveImageAsync(httpClient, images[i], outputFormat, i + 1, cts.Token);
                saved.Add(savedImage);
            }

            return saved;
        }

        private async Task<GeneratedImagePayload> SaveImageAsync(
            HttpClient httpClient,
            ImageResponseData image,
            string outputFormat,
            int index,
            CancellationToken cancellationToken) {
            byte[] bytes;
            string contentType;
            string extension;

            if (!string.IsNullOrWhiteSpace(image.Base64Data)) {
                bytes = Convert.FromBase64String(NormalizeBase64Padding(image.Base64Data));
                contentType = image.ContentType ?? ContentTypeFromOutputFormat(outputFormat);
                extension = ExtensionFromContentType(contentType) ?? ExtensionFromOutputFormat(outputFormat);
            } else if (!string.IsNullOrWhiteSpace(image.Url)) {
                if (TryParseImageDataUrl(image.Url, out var dataUrlBytes, out var dataUrlContentType)) {
                    bytes = dataUrlBytes;
                    contentType = dataUrlContentType;
                    extension = ExtensionFromContentType(contentType) ?? ExtensionFromOutputFormat(outputFormat);
                } else {
                    using var response = await httpClient.GetAsync(image.Url, cancellationToken);
                    if (!response.IsSuccessStatusCode) {
                        throw new InvalidOperationException($"Unable to download generated image: HTTP {( int ) response.StatusCode}.");
                    }

                    bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    contentType = response.Content.Headers.ContentType?.MediaType ?? ContentTypeFromOutputFormat(outputFormat);
                    extension = ExtensionFromContentType(contentType)
                        ?? ExtensionFromUrl(image.Url)
                        ?? ExtensionFromOutputFormat(outputFormat);
                }
            } else {
                throw new InvalidOperationException("The image API response did not include image data.");
            }

            var directory = Path.Combine(Env.WorkDir, "GeneratedImages", DateTimeOffset.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}_{index}{extension}");
            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

            return new GeneratedImagePayload(
                bytes,
                new GeneratedImageInfo {
                    FilePath = filePath,
                    FileSizeBytes = bytes.LongLength,
                    ContentType = contentType
                });
        }

        private async Task<SendPhotoResult> SendGeneratedImageAsync(
            byte[] imageBytes,
            string fileName,
            ToolContext toolContext,
            string caption,
            long? replyToMessageId) {
            try {
                if (toolContext == null || toolContext.ChatId == 0) {
                    return new SendPhotoResult {
                        Success = false,
                        Error = "Cannot send generated image because chat context is missing."
                    };
                }

                if (imageBytes.LongLength > TelegramPhotoLimitBytes) {
                    return new SendPhotoResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = $"Generated image is too large for Telegram photo upload ({imageBytes.LongLength / 1024 / 1024}MB)."
                    };
                }

                var photo = InputFile.FromStream(new MemoryStream(imageBytes), fileName);
                var replyParameters = GetReplyParameters(replyToMessageId, toolContext);
                var safeCaption = string.IsNullOrWhiteSpace(caption)
                    ? null
                    : caption.Length > 1024 ? caption.Substring(0, 1024) : caption;

                using var cts = new CancellationTokenSource(SendTimeout);
                var message = await _sendMessage.AddTaskWithResult(async () => await _botClient.SendPhoto(
                    chatId: toolContext.ChatId,
                    photo: photo,
                    caption: safeCaption,
                    replyParameters: replyParameters,
                    cancellationToken: cts.Token
                ), toolContext.ChatId);

                return new SendPhotoResult {
                    Success = true,
                    MessageId = message.MessageId,
                    ChatId = message.Chat.Id
                };
            } catch (Exception ex) {
                return new SendPhotoResult {
                    Success = false,
                    ChatId = toolContext?.ChatId ?? 0,
                    Error = $"Failed to send generated image: {ex.Message}"
                };
            }
        }

        private static ReplyParameters GetReplyParameters(long? explicitReplyToMessageId, ToolContext toolContext) {
            long? messageId = explicitReplyToMessageId ?? ( toolContext.MessageId != 0 ? toolContext.MessageId : ( long? ) null );
            return messageId.HasValue ? new ReplyParameters { MessageId = ( int ) messageId.Value } : null;
        }

        internal static JObject BuildMiniMaxImageGenerationRequestBody(
            string prompt,
            string model,
            string size,
            int count,
            string responseFormat,
            string aspectRatio,
            long? seed,
            bool promptOptimizer,
            bool aigcWatermark,
            string styleType,
            double? styleWeight) {
            if (string.IsNullOrWhiteSpace(prompt)) {
                throw new ArgumentException("MiniMax prompt cannot be empty.", nameof(prompt));
            }

            if (prompt.Length > 1500) {
                throw new ArgumentException("MiniMax prompt cannot exceed 1500 characters.", nameof(prompt));
            }

            var normalizedModel = string.IsNullOrWhiteSpace(model)
                ? "image-01"
                : model.Trim();
            if (!IsMiniMaxImageModel(normalizedModel)) {
                throw new ArgumentException("MiniMax image generation model must be image-01 or image-01-live.", nameof(model));
            }

            var normalizedResponseFormat = NormalizeChoice(responseFormat, "minimaxResponseFormat", DefaultMiniMaxResponseFormat, "url", "base64");
            var requestBody = new JObject {
                ["model"] = normalizedModel,
                ["prompt"] = prompt.Trim(),
                ["n"] = Math.Clamp(count, 1, MaxImageCount),
                ["response_format"] = normalizedResponseFormat,
                ["prompt_optimizer"] = promptOptimizer,
                ["aigc_watermark"] = aigcWatermark
            };

            if (seed.HasValue) {
                requestBody["seed"] = seed.Value;
            }

            var normalizedAspectRatio = NormalizeMiniMaxAspectRatio(aspectRatio, normalizedModel);
            if (!string.IsNullOrWhiteSpace(normalizedAspectRatio)) {
                requestBody["aspect_ratio"] = normalizedAspectRatio;
            } else if (normalizedModel.Equals("image-01", StringComparison.OrdinalIgnoreCase) &&
                       TryParseMiniMaxSize(size, out var width, out var height)) {
                requestBody["width"] = width;
                requestBody["height"] = height;
            }

            var normalizedStyleType = NormalizeMiniMaxStyleType(styleType, normalizedModel);
            if (!string.IsNullOrWhiteSpace(normalizedStyleType)) {
                var style = new JObject {
                    ["style_type"] = normalizedStyleType
                };
                if (styleWeight.HasValue) {
                    if (styleWeight.Value <= 0 || styleWeight.Value > 1) {
                        throw new ArgumentException("MiniMax styleWeight must be in (0, 1].", nameof(styleWeight));
                    }

                    style["style_weight"] = styleWeight.Value;
                }

                requestBody["style"] = style;
            } else if (styleWeight.HasValue) {
                throw new ArgumentException("MiniMax styleWeight requires styleType.", nameof(styleWeight));
            }

            return requestBody;
        }

        internal static void EnsureMiniMaxResponseSucceeded(string responseText) {
            if (string.IsNullOrWhiteSpace(responseText)) {
                throw new InvalidOperationException("MiniMax image API returned an empty response body.");
            }

            var token = JToken.Parse(responseText);
            var statusToken = token.SelectToken("$.base_resp.status_code");
            if (statusToken == null || statusToken.Type == JTokenType.Null) {
                return;
            }

            var statusCode = statusToken.Value<int>();
            if (statusCode == 0) {
                return;
            }

            var statusMessage = token.SelectToken("$.base_resp.status_msg")?.Value<string>();
            throw new InvalidOperationException($"MiniMax image API returned status {statusCode}: {statusMessage ?? "unknown error"}");
        }

        internal static List<ImageResponseData> ExtractImages(string responseText) {
            var token = JToken.Parse(responseText);
            var images = new List<ImageResponseData>();
            var dataArray = token["data"] as JArray;

            if (dataArray != null) {
                foreach (var item in dataArray.OfType<JObject>()) {
                    images.AddRange(ExtractImagesFromObject(item));
                }
            }

            if (images.Count == 0 && token["data"] is JObject dataObject) {
                images.AddRange(ExtractImagesFromObject(dataObject));
            }

            if (images.Count == 0) {
                foreach (var item in EnumerateObjects(token)) {
                    images.AddRange(ExtractImagesFromObject(item));
                }
            }

            return images;
        }

        private static IEnumerable<JObject> EnumerateObjects(JToken token) {
            if (token is JObject obj) {
                yield return obj;
            }

            foreach (var child in token.Children()) {
                foreach (var item in EnumerateObjects(child)) {
                    yield return item;
                }
            }
        }

        private static IEnumerable<ImageResponseData> ExtractImagesFromObject(JObject obj) {
            foreach (var url in GetStringArrayValues(obj, "image_urls")) {
                yield return new ImageResponseData(null, url, null);
            }

            foreach (var base64 in GetStringArrayValues(obj, "image_base64")) {
                if (TryParseImageDataUrl(base64, out _, out var contentType, out var strippedBase64)) {
                    yield return new ImageResponseData(strippedBase64, null, contentType);
                } else {
                    yield return new ImageResponseData(base64, null, null);
                }
            }

            var image = ExtractImageFromObject(obj);
            if (image != null) {
                yield return image;
            }
        }

        private static ImageResponseData ExtractImageFromObject(JObject obj) {
            var base64 = GetFirstString(obj, "b64_json", "b64", "base64", "image_b64", "image_base64", "partial_image_b64");
            if (!string.IsNullOrWhiteSpace(base64)) {
                if (TryParseImageDataUrl(base64, out _, out var contentType, out var strippedBase64)) {
                    base64 = strippedBase64;
                    return new ImageResponseData(base64, null, contentType);
                }

                return new ImageResponseData(base64, null, null);
            }

            var url = GetFirstString(obj, "url", "image_url", "output_url");
            if (!string.IsNullOrWhiteSpace(url)) {
                return new ImageResponseData(null, url, null);
            }

            return null;
        }

        private static IEnumerable<string> GetStringArrayValues(JObject obj, params string[] keys) {
            foreach (var key in keys) {
                if (!obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token) ||
                    token is not JArray values) {
                    continue;
                }

                foreach (var item in values) {
                    var value = item.Type == JTokenType.String ? item.Value<string>()?.Trim() : null;
                    if (!string.IsNullOrWhiteSpace(value)) {
                        yield return value;
                    }
                }
            }
        }

        private static string GetFirstString(JObject obj, params string[] keys) {
            foreach (var key in keys) {
                if (obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token) &&
                    token.Type == JTokenType.String) {
                    var value = token.Value<string>()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value)) {
                        return value;
                    }
                }
            }

            return null;
        }

        private static bool IsMiniMaxImageGeneration(LLMChannel channel, string modelName) {
            return channel?.Provider == LLMProvider.MiniMax || IsMiniMaxImageModel(modelName);
        }

        private static bool IsMiniMaxImageModel(string modelName) {
            return MiniMaxImageModels.Contains(modelName?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeMiniMaxAspectRatio(string aspectRatio, string modelName) {
            if (string.IsNullOrWhiteSpace(aspectRatio)) {
                return null;
            }

            var normalized = aspectRatio.Trim();
            if (!MiniMaxAspectRatios.Contains(normalized, StringComparer.OrdinalIgnoreCase)) {
                throw new ArgumentException($"MiniMax aspectRatio must be one of: {string.Join(", ", MiniMaxAspectRatios)}.", nameof(aspectRatio));
            }

            if (normalized.Equals("21:9", StringComparison.OrdinalIgnoreCase) &&
                !modelName.Equals("image-01", StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException("MiniMax aspectRatio 21:9 is only supported by image-01.", nameof(aspectRatio));
            }

            return normalized;
        }

        private static string NormalizeMiniMaxStyleType(string styleType, string modelName) {
            if (string.IsNullOrWhiteSpace(styleType)) {
                return null;
            }

            if (!modelName.Equals("image-01-live", StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException("MiniMax styleType is only supported by image-01-live.", nameof(styleType));
            }

            var normalized = styleType.Trim();
            if (!MiniMaxStyleTypes.Contains(normalized, StringComparer.Ordinal)) {
                throw new ArgumentException($"MiniMax styleType must be one of: {string.Join(", ", MiniMaxStyleTypes)}.", nameof(styleType));
            }

            return normalized;
        }

        private static bool TryParseMiniMaxSize(string size, out int width, out int height) {
            width = 0;
            height = 0;
            if (string.IsNullOrWhiteSpace(size) || size.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            var match = Regex.Match(size.Trim(), @"^(?<width>[1-9]\d{1,4})x(?<height>[1-9]\d{1,4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success ||
                !int.TryParse(match.Groups["width"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out width) ||
                !int.TryParse(match.Groups["height"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out height)) {
                throw new ArgumentException("MiniMax size must be auto or a WIDTHxHEIGHT value, for example 1024x1024.", nameof(size));
            }

            if (!IsValidMiniMaxDimension(width) || !IsValidMiniMaxDimension(height)) {
                throw new ArgumentException("MiniMax width and height must be in [512, 2048] and multiples of 8.", nameof(size));
            }

            return true;
        }

        private static bool IsValidMiniMaxDimension(int value) {
            return value >= 512 && value <= 2048 && value % 8 == 0;
        }

        private static string ExtractErrorMessage(string responseText) {
            if (string.IsNullOrWhiteSpace(responseText)) {
                return "empty response body";
            }

            try {
                var token = JToken.Parse(responseText);
                var message = token.SelectToken("$.error.message")?.Value<string>()
                    ?? token.SelectToken("$.message")?.Value<string>();
                if (!string.IsNullOrWhiteSpace(message)) {
                    return message;
                }
            } catch {
                // fall through to truncated raw body
            }

            return responseText.Length <= 2000 ? responseText : responseText.Substring(0, 2000);
        }

        private static bool TryParseImageDataUrl(string value, out byte[] bytes, out string contentType) {
            var matched = TryParseImageDataUrl(value, out bytes, out contentType, out _);
            return matched;
        }

        private static bool TryParseImageDataUrl(string value, out byte[] bytes, out string contentType, out string base64Data) {
            bytes = null;
            contentType = null;
            base64Data = null;
            if (string.IsNullOrWhiteSpace(value)) {
                return false;
            }

            var match = Regex.Match(value.Trim(), @"^data:(image\/[a-z0-9.+-]+);base64,(.+)$", RegexOptions.IgnoreCase);
            if (!match.Success) {
                return false;
            }

            contentType = match.Groups[1].Value;
            base64Data = match.Groups[2].Value;
            bytes = Convert.FromBase64String(NormalizeBase64Padding(base64Data));
            return true;
        }

        private static string NormalizeBase64Padding(string value) {
            var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
            var padding = normalized.Length % 4;
            return padding == 0 ? normalized : normalized.PadRight(normalized.Length + ( 4 - padding ), '=');
        }

        private static string NormalizeSize(string size) {
            if (string.IsNullOrWhiteSpace(size)) {
                return DefaultSize;
            }

            var normalized = size.Trim().ToLowerInvariant();
            if (normalized == "auto") {
                return normalized;
            }

            if (!Regex.IsMatch(normalized, @"^[1-9]\d{1,4}x[1-9]\d{1,4}$", RegexOptions.CultureInvariant)) {
                throw new ArgumentException("size must be auto or a WIDTHxHEIGHT value, for example 1024x1024.");
            }

            return normalized;
        }

        private static string NormalizeChoice(string value, string parameterName, string defaultValue, params string[] allowed) {
            var normalized = string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim().ToLowerInvariant();
            if (!allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase)) {
                throw new ArgumentException($"{parameterName} must be one of: {string.Join(", ", allowed)}.");
            }

            return normalized;
        }

        private static string NormalizeOptionalChoice(string value, string parameterName, params string[] allowed) {
            if (string.IsNullOrWhiteSpace(value)) {
                return null;
            }

            return NormalizeChoice(value, parameterName, null, allowed);
        }

        private static string ContentTypeFromOutputFormat(string outputFormat) {
            return outputFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : $"image/{outputFormat.ToLowerInvariant()}";
        }

        private static string ExtensionFromOutputFormat(string outputFormat) {
            return outputFormat.ToLowerInvariant() switch {
                "jpeg" or "jpg" => ".jpg",
                "webp" => ".webp",
                _ => ".png"
            };
        }

        private static string ExtensionFromContentType(string contentType) {
            return contentType?.ToLowerInvariant() switch {
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/webp" => ".webp",
                "image/png" => ".png",
                _ => null
            };
        }

        private static string ExtensionFromUrl(string url) {
            try {
                var extension = Path.GetExtension(new Uri(url).AbsolutePath);
                return extension?.ToLowerInvariant() switch {
                    ".jpg" or ".jpeg" => ".jpg",
                    ".png" => ".png",
                    ".webp" => ".webp",
                    _ => null
                };
            } catch {
                return null;
            }
        }

        internal sealed record ImageResponseData(string Base64Data, string Url, string ContentType);
        private sealed record GeneratedImagePayload(byte[] Bytes, GeneratedImageInfo Info);
    }
}

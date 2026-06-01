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
using Telegram.Bot.Types.Enums;
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
    public class MusicGenerationToolSettingsService : IService {
        public const string EnableToolKey = "LLM:EnableMusicGenerationTool";
        public const string ModelNameKey = "LLM:MusicGenerationModelName";
        public const string DefaultModelName = "music-2.6";

        public string ServiceName => "MusicGenerationToolSettingsService";

        private readonly DataDbContext _dbContext;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly ILogger<MusicGenerationToolSettingsService> _logger;

        public MusicGenerationToolSettingsService(
            DataDbContext dbContext,
            IConnectionMultiplexer connectionMultiplexer,
            ILogger<MusicGenerationToolSettingsService> logger) {
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
                    .Select(x => x.MusicGenerationModelName)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(groupModelName)) {
                    return groupModelName.Trim();
                }
            }

            return await GetDefaultModelNameAsync();
        }

        public async Task SetDefaultModelNameAsync(string modelName) {
            if (string.IsNullOrWhiteSpace(modelName)) {
                throw new ArgumentException("Music generation model name cannot be empty.", nameof(modelName));
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
                throw new ArgumentException("Music generation model name cannot be empty.", nameof(modelName));
            }

            var normalizedModelName = modelName.Trim();
            var defaultModelName = await GetDefaultModelNameAsync();
            var settings = await _dbContext.GroupSettings
                .FirstOrDefaultAsync(x => x.GroupId == chatId);
            var previous = settings == null ? null : settings.MusicGenerationModelName;
            GroupSettings newSettings = null;

            if (settings == null) {
                newSettings = new GroupSettings {
                    GroupId = chatId,
                    MusicGenerationModelName = normalizedModelName
                };
                await _dbContext.GroupSettings.AddAsync(newSettings);
            } else {
                settings.MusicGenerationModelName = normalizedModelName;
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

                previous = settings.MusicGenerationModelName;
                settings.MusicGenerationModelName = normalizedModelName;
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
                settings.MusicGenerationModelName = null;
                await _dbContext.SaveChangesAsync();
            }

            return await GetDefaultModelNameAsync();
        }

        private static void ApplyToolVisibility(bool enabled) {
            McpToolHelper.SetBuiltInToolEnabled(MusicGenerationToolService.ToolName, enabled);
        }

        private async Task RefreshAgentToolDefinitionsAsync() {
            try {
                await McpToolHelper.RefreshAgentToolDefsInRedisAsync(_connectionMultiplexer);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to refresh agent tool definitions after music generation tool visibility changed.");
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
    public class MusicGenerationToolService : IService {
        public const string ToolName = "generate_music";
        private const string DefaultOutputFormat = "hex";
        private const int DefaultSampleRate = 44100;
        private const int DefaultBitrate = 256000;
        private const string DefaultAudioFormat = "mp3";
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(120);
        private static readonly string[] MiniMaxMusicModels = { "music-2.6", "music-cover", "music-2.6-free", "music-cover-free" };
        private static readonly int[] AllowedSampleRates = { 16000, 24000, 32000, 44100 };
        private static readonly int[] AllowedBitrates = { 32000, 64000, 128000, 256000 };
        private static readonly string[] AllowedAudioFormats = { "mp3", "wav", "pcm" };
        internal const string AcquireChannelSemaphoreScript = @"
local key = KEYS[1]
local limit = tonumber(ARGV[1])
local current = tonumber(redis.call('GET', key) or '0')
if current < 0 then
  redis.call('SET', key, 0)
  current = 0
end
if current < limit then
  redis.call('INCR', key)
  return 1
end
return 0";

        public string ServiceName => "MusicGenerationToolService";

        private readonly DataDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly MusicGenerationToolSettingsService _settingsService;
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendMessage;
        private readonly ILogger<MusicGenerationToolService> _logger;

        public MusicGenerationToolService(
            DataDbContext dbContext,
            IHttpClientFactory httpClientFactory,
            IConnectionMultiplexer connectionMultiplexer,
            MusicGenerationToolSettingsService settingsService,
            ITelegramBotClient botClient,
            SendMessage sendMessage,
            ILogger<MusicGenerationToolService> logger) {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
            _connectionMultiplexer = connectionMultiplexer;
            _settingsService = settingsService;
            _botClient = botClient;
            _sendMessage = sendMessage;
            _logger = logger;
        }

        [BuiltInTool(@"Generate music through the configured MiniMax music API and optionally send the resulting audio to the current Telegram chat.
The default model is the current chat's configured music generation model, falling back to the bot-wide default music-2.6 when the chat has no music model configured. Supported MiniMax models are music-2.6, music-2.6-free, music-cover, and music-cover-free. The API base URL and API key are read from the configured LLM channel for the selected music model, so administrators can use https://api.minimaxi.com or a compatible gateway.
Use this when the user asks you to compose, generate, create, or cover a song/music track. For text-to-music, provide lyrics unless lyricsOptimizer is true or isInstrumental is true. The tool saves generated audio under the bot work directory and sends it back to the current chat by default.", Name = ToolName)]
        public async Task<MusicGenerationResult> GenerateMusic(
            ToolContext toolContext,
            [BuiltInParameter("Music description for style, mood, scene, instrumentation, language, vocal style, or arrangement. Required for instrumental tracks and lyricsOptimizer without lyrics. MiniMax limit: 2000 characters for music-2.6, 10-300 for music-cover.", IsRequired = false)] string prompt = null,
            [BuiltInParameter("Lyrics separated by \\n. Supports structure tags such as [Intro], [Verse], [Pre Chorus], [Chorus], [Bridge], [Outro], [Hook], [Inst], and [Solo]. Required for non-instrumental text-to-music unless lyricsOptimizer is true.", IsRequired = false)] string lyrics = null,
            [BuiltInParameter("MiniMax music model. Defaults to the current chat's configured music generation model, or the bot-wide default music-2.6 when unset. Use music-2.6-free if the account only has free MiniMax music access.", IsRequired = false)] string model = null,
            [BuiltInParameter("Whether to generate instrumental music with no vocals. Only supported by music-2.6 and music-2.6-free. Defaults to false.", IsRequired = false)] bool isInstrumental = false,
            [BuiltInParameter("Whether MiniMax should generate/optimize lyrics from the prompt. Only supported by music-2.6 and music-2.6-free. If true and lyrics is empty, prompt must describe the song. Defaults to false.", IsRequired = false)] bool lyricsOptimizer = false,
            [BuiltInParameter("Output format returned by MiniMax: hex or url. Defaults to hex. URL results are downloaded immediately because MiniMax URLs expire.", IsRequired = false)] string outputFormat = DefaultOutputFormat,
            [BuiltInParameter("Audio sample rate: 16000, 24000, 32000, or 44100. Defaults to 44100.", IsRequired = false)] int sampleRate = DefaultSampleRate,
            [BuiltInParameter("Audio bitrate: 32000, 64000, 128000, or 256000. Defaults to 256000.", IsRequired = false)] int bitrate = DefaultBitrate,
            [BuiltInParameter("Audio encoding format: mp3, wav, or pcm. Defaults to mp3.", IsRequired = false)] string format = DefaultAudioFormat,
            [BuiltInParameter("Whether to add the MiniMax AIGC watermark at the end of the audio. Defaults to false.", IsRequired = false)] bool aigcWatermark = false,
            [BuiltInParameter("Reference audio URL for music-cover/music-cover-free. For cover models, provide exactly one of audioUrl, audioBase64, or coverFeatureId.", IsRequired = false)] string audioUrl = null,
            [BuiltInParameter("Base64 encoded reference audio for music-cover/music-cover-free. For cover models, provide exactly one of audioUrl, audioBase64, or coverFeatureId.", IsRequired = false)] string audioBase64 = null,
            [BuiltInParameter("Feature ID returned by MiniMax cover preprocess for two-step cover generation. For cover models, provide exactly one of audioUrl, audioBase64, or coverFeatureId. Lyrics is required when this is used.", IsRequired = false)] string coverFeatureId = null,
            [BuiltInParameter("Whether to send generated music to the current Telegram chat. Defaults to true.", IsRequired = false)] bool sendToChat = true,
            [BuiltInParameter("Optional Telegram caption for the generated audio/document. Keep it short.", IsRequired = false)] string caption = null,
            [BuiltInParameter("Optional Telegram audio title. Defaults to a generated file name.", IsRequired = false)] string title = null,
            [BuiltInParameter("Optional Telegram audio performer/artist.", IsRequired = false)] string performer = null,
            [BuiltInParameter("Optional Telegram message ID to reply to. Defaults to the original user message.", IsRequired = false)] long? replyToMessageId = null,
            [BuiltInParameter("Request timeout in seconds, from 60 to 900. Defaults to 600.", IsRequired = false)] int timeoutSeconds = 600) {
            var result = new MusicGenerationResult();

            try {
                if (!await _settingsService.IsToolEnabledAsync()) {
                    return Failure("Music generation tool is disabled by the administrator.");
                }

                var effectiveModel = string.IsNullOrWhiteSpace(model)
                    ? await _settingsService.GetModelNameAsync(toolContext?.ChatId ?? 0)
                    : model.Trim();
                var normalizedOutputFormat = NormalizeChoice(outputFormat, "outputFormat", DefaultOutputFormat, "url", "hex");
                var normalizedAudioFormat = NormalizeChoice(format, "format", DefaultAudioFormat, AllowedAudioFormats);
                var normalizedTimeoutSeconds = Math.Clamp(timeoutSeconds, 60, 900);
                ValidateAudioSetting(sampleRate, bitrate);

                result.Model = effectiveModel;

                var channels = await LoadChannelsAsync(effectiveModel);
                if (channels.Count == 0) {
                    return Failure(
                        $"No channel is configured for music model '{effectiveModel}'. Add a MiniMax channel and associate model '{effectiveModel}' with it. The channel gateway can be https://api.minimaxi.com or a compatible MiniMax gateway.");
                }

                Exception lastException = null;
                foreach (var channel in channels) {
                    if (!await TryAcquireChannelAsync(channel)) {
                        continue;
                    }

                    try {
                        var endpoint = BuildMiniMaxMusicGenerationEndpoint(channel.Gateway);
                        var generated = await RequestMiniMaxMusicAsync(
                            endpoint,
                            channel,
                            effectiveModel,
                            prompt,
                            lyrics,
                            isInstrumental,
                            lyricsOptimizer,
                            normalizedOutputFormat,
                            sampleRate,
                            bitrate,
                            normalizedAudioFormat,
                            aigcWatermark,
                            audioUrl,
                            audioBase64,
                            coverFeatureId,
                            normalizedTimeoutSeconds);

                        result.ChannelId = channel.Id;
                        result.ChannelName = channel.Name;
                        result.Endpoint = endpoint;
                        result.Music = generated.Info;

                        if (sendToChat) {
                            result.SentAudio = await SendGeneratedMusicAsync(
                                generated.Bytes,
                                Path.GetFileName(generated.Info.FilePath),
                                generated.Info,
                                toolContext,
                                caption,
                                title,
                                performer,
                                replyToMessageId);
                        }

                        result.Success = true;
                        if (result.SentAudio != null && !result.SentAudio.Success) {
                            result.Error = "Generated music file was saved, but Telegram audio send failed.";
                        }

                        return result;
                    } catch (Exception ex) {
                        lastException = ex;
                        _logger.LogWarning(ex, "Music generation failed for channel {ChannelId} ({ChannelName}) and model {Model}.", channel.Id, channel.Name, effectiveModel);
                    } finally {
                        await ReleaseChannelAsync(channel);
                    }
                }

                return Failure(lastException == null
                    ? $"All channels for music model '{effectiveModel}' are currently at capacity."
                    : $"Music generation failed for all configured channels: {lastException.Message}");
            } catch (Exception ex) {
                _logger.LogError(ex, "Music generation tool failed.");
                return Failure(ex.Message);
            }

            MusicGenerationResult Failure(string error) {
                return new MusicGenerationResult {
                    Success = false,
                    Error = error,
                    Model = result.Model
                };
            }
        }

        public static string BuildMiniMaxMusicGenerationEndpoint(string gateway) {
            if (string.IsNullOrWhiteSpace(gateway)) {
                throw new ArgumentException("MiniMax music generation channel gateway cannot be empty.", nameof(gateway));
            }

            var normalized = gateway.Trim().TrimEnd('/');
            if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
                return $"{normalized}/music_generation";
            }

            if (normalized.EndsWith("/v1/music_generation", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith("/music_generation", StringComparison.OrdinalIgnoreCase)) {
                return normalized;
            }

            return $"{normalized}/v1/music_generation";
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
            var limit = Math.Max(1, channel.Parallel);
            var result = await redisDb.ScriptEvaluateAsync(
                AcquireChannelSemaphoreScript,
                new RedisKey[] { key },
                new RedisValue[] { limit });
            return ( int ) result == 1;
        }

        private async Task ReleaseChannelAsync(LLMChannel channel) {
            try {
                await _connectionMultiplexer.GetDatabase().StringDecrementAsync(GetSemaphoreKey(channel.Id));
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to release music generation semaphore for channel {ChannelId}.", channel.Id);
            }
        }

        private static string GetSemaphoreKey(int channelId) => $"llm:channel:{channelId}:music_generation_semaphore";

        private async Task<GeneratedMusicPayload> RequestMiniMaxMusicAsync(
            string endpoint,
            LLMChannel channel,
            string model,
            string prompt,
            string lyrics,
            bool isInstrumental,
            bool lyricsOptimizer,
            string outputFormat,
            int sampleRate,
            int bitrate,
            string format,
            bool aigcWatermark,
            string audioUrl,
            string audioBase64,
            string coverFeatureId,
            int timeoutSeconds) {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var requestBody = BuildMiniMaxMusicGenerationRequestBody(
                model,
                prompt,
                lyrics,
                isInstrumental,
                lyricsOptimizer,
                outputFormat,
                sampleRate,
                bitrate,
                format,
                aigcWatermark,
                audioUrl,
                audioBase64,
                coverFeatureId);

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
                throw new InvalidOperationException($"MiniMax music API returned HTTP {( int ) response.StatusCode}: {ExtractErrorMessage(responseText)}");
            }

            EnsureMiniMaxResponseSucceeded(responseText);
            var music = ExtractMusic(responseText, outputFormat, format);
            return await SaveMusicAsync(httpClient, music, format, cts.Token);
        }

        private async Task<GeneratedMusicPayload> SaveMusicAsync(
            HttpClient httpClient,
            MusicResponseData music,
            string requestedFormat,
            CancellationToken cancellationToken) {
            byte[] bytes;
            string contentType;
            string extension;

            if (music.Bytes != null) {
                bytes = music.Bytes;
                contentType = ContentTypeFromAudioFormat(requestedFormat);
                extension = ExtensionFromContentType(contentType) ?? ExtensionFromAudioFormat(requestedFormat);
            } else if (!string.IsNullOrWhiteSpace(music.Url)) {
                using var response = await httpClient.GetAsync(music.Url, cancellationToken);
                if (!response.IsSuccessStatusCode) {
                    throw new InvalidOperationException($"Unable to download generated music: HTTP {( int ) response.StatusCode}.");
                }

                bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                contentType = response.Content.Headers.ContentType?.MediaType ?? ContentTypeFromAudioFormat(requestedFormat);
                extension = ExtensionFromContentType(contentType)
                    ?? ExtensionFromUrl(music.Url)
                    ?? ExtensionFromAudioFormat(requestedFormat);
            } else {
                throw new InvalidOperationException("The MiniMax music API response did not include audio data.");
            }

            var directory = Path.Combine(Env.WorkDir, "GeneratedMusic", DateTimeOffset.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}");
            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

            return new GeneratedMusicPayload(
                bytes,
                new GeneratedMusicInfo {
                    FilePath = filePath,
                    FileSizeBytes = bytes.LongLength,
                    ContentType = contentType,
                    DurationMilliseconds = music.DurationMilliseconds,
                    SampleRate = music.SampleRate,
                    Channels = music.Channels,
                    Bitrate = music.Bitrate
                });
        }

        private async Task<SendAudioResult> SendGeneratedMusicAsync(
            byte[] audioBytes,
            string fileName,
            GeneratedMusicInfo info,
            ToolContext toolContext,
            string caption,
            string title,
            string performer,
            long? replyToMessageId) {
            try {
                if (toolContext == null || toolContext.ChatId == 0) {
                    return new SendAudioResult {
                        Success = false,
                        Error = "Cannot send generated music because chat context is missing."
                    };
                }

                long maxFileSizeBytes = Env.IsLocalAPI
                    ? 2L * 1024 * 1024 * 1024
                    : 50 * 1024 * 1024;
                if (audioBytes.LongLength > maxFileSizeBytes) {
                    return new SendAudioResult {
                        Success = false,
                        ChatId = toolContext.ChatId,
                        Error = $"Generated music is too large for Telegram upload ({audioBytes.LongLength / 1024 / 1024}MB). Maximum allowed size is {( Env.IsLocalAPI ? "2GB" : "50MB" )}."
                    };
                }

                var replyParameters = GetReplyParameters(replyToMessageId, toolContext);
                var safeCaption = string.IsNullOrWhiteSpace(caption)
                    ? null
                    : caption.Length > 1024 ? caption.Substring(0, 1024) : caption;
                var safeTitle = string.IsNullOrWhiteSpace(title)
                    ? Path.GetFileNameWithoutExtension(fileName)
                    : title.Trim();
                var safePerformer = string.IsNullOrWhiteSpace(performer) ? null : performer.Trim();

                using var cts = new CancellationTokenSource(SendTimeout);
                if (IsTelegramAudioContentType(info.ContentType)) {
                    var audio = InputFile.FromStream(new MemoryStream(audioBytes), fileName);
                    var message = await _sendMessage.AddTaskWithResult(async () => await _botClient.SendAudio(
                        chatId: toolContext.ChatId,
                        audio: audio,
                        caption: safeCaption,
                        parseMode: ParseMode.Html,
                        duration: info.DurationMilliseconds.HasValue ? info.DurationMilliseconds.Value / 1000 : null,
                        performer: safePerformer,
                        title: safeTitle,
                        replyParameters: replyParameters,
                        cancellationToken: cts.Token
                    ), toolContext.ChatId);

                    return new SendAudioResult {
                        Success = true,
                        MessageId = message.MessageId,
                        ChatId = message.Chat.Id
                    };
                }

                var document = InputFile.FromStream(new MemoryStream(audioBytes), fileName);
                var sentDocument = await _sendMessage.AddTaskWithResult(async () => await _botClient.SendDocument(
                    chatId: toolContext.ChatId,
                    document: document,
                    caption: safeCaption,
                    parseMode: ParseMode.Html,
                    replyParameters: replyParameters,
                    cancellationToken: cts.Token
                ), toolContext.ChatId);

                return new SendAudioResult {
                    Success = true,
                    MessageId = sentDocument.MessageId,
                    ChatId = sentDocument.Chat.Id,
                    SentAsDocument = true
                };
            } catch (Exception ex) {
                return new SendAudioResult {
                    Success = false,
                    ChatId = toolContext?.ChatId ?? 0,
                    Error = $"Failed to send generated music: {ex.Message}"
                };
            }
        }

        private static ReplyParameters GetReplyParameters(long? explicitReplyToMessageId, ToolContext toolContext) {
            long? messageId = explicitReplyToMessageId ?? ( toolContext.MessageId != 0 ? toolContext.MessageId : ( long? ) null );
            return messageId.HasValue ? new ReplyParameters { MessageId = ( int ) messageId.Value } : null;
        }

        internal static JObject BuildMiniMaxMusicGenerationRequestBody(
            string model,
            string prompt,
            string lyrics,
            bool isInstrumental,
            bool lyricsOptimizer,
            string outputFormat,
            int sampleRate,
            int bitrate,
            string format,
            bool aigcWatermark,
            string audioUrl,
            string audioBase64,
            string coverFeatureId) {
            var normalizedModel = string.IsNullOrWhiteSpace(model)
                ? MusicGenerationToolSettingsService.DefaultModelName
                : model.Trim();
            if (!IsMiniMaxMusicModel(normalizedModel)) {
                throw new ArgumentException($"MiniMax music generation model must be one of: {string.Join(", ", MiniMaxMusicModels)}.", nameof(model));
            }

            var normalizedOutputFormat = NormalizeChoice(outputFormat, "outputFormat", DefaultOutputFormat, "url", "hex");
            var normalizedFormat = NormalizeChoice(format, "format", DefaultAudioFormat, AllowedAudioFormats);
            ValidateAudioSetting(sampleRate, bitrate);

            var isCoverModel = IsMiniMaxCoverModel(normalizedModel);
            var requestBody = new JObject {
                ["model"] = normalizedModel,
                ["output_format"] = normalizedOutputFormat,
                ["audio_setting"] = new JObject {
                    ["sample_rate"] = sampleRate,
                    ["bitrate"] = bitrate,
                    ["format"] = normalizedFormat
                },
                ["aigc_watermark"] = aigcWatermark
            };

            if (isCoverModel) {
                AppendCoverRequestFields(requestBody, prompt, lyrics, audioUrl, audioBase64, coverFeatureId, isInstrumental, lyricsOptimizer);
            } else {
                AppendTextToMusicRequestFields(requestBody, prompt, lyrics, isInstrumental, lyricsOptimizer);
            }

            return requestBody;
        }

        internal static void EnsureMiniMaxResponseSucceeded(string responseText) {
            if (string.IsNullOrWhiteSpace(responseText)) {
                throw new InvalidOperationException("MiniMax music API returned an empty response body.");
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
            throw new InvalidOperationException($"MiniMax music API returned status {statusCode}: {statusMessage ?? "unknown error"}");
        }

        internal static MusicResponseData ExtractMusic(string responseText, string outputFormat, string requestedFormat) {
            if (string.IsNullOrWhiteSpace(responseText)) {
                throw new InvalidOperationException("MiniMax music API returned an empty response body.");
            }

            var token = JToken.Parse(responseText);
            var status = token.SelectToken("$.data.status")?.Value<int?>();
            if (status == 1) {
                throw new InvalidOperationException("MiniMax music API reports that synthesis is still in progress and did not return completed audio.");
            }

            var audio = token.SelectToken("$.data.audio")?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(audio)) {
                throw new InvalidOperationException("MiniMax music API response did not include data.audio.");
            }

            byte[] bytes = null;
            string url = null;
            if (IsHttpUrl(audio)) {
                url = audio;
            } else if (LooksLikeHex(audio) || string.Equals(outputFormat, "hex", StringComparison.OrdinalIgnoreCase)) {
                bytes = HexToBytes(audio);
            } else {
                url = audio;
            }

            return new MusicResponseData(
                bytes,
                url,
                token.SelectToken("$.extra_info.music_duration")?.Value<int?>(),
                token.SelectToken("$.extra_info.music_sample_rate")?.Value<int?>(),
                token.SelectToken("$.extra_info.music_channel")?.Value<int?>(),
                token.SelectToken("$.extra_info.bitrate")?.Value<int?>(),
                requestedFormat);
        }

        private static void AppendTextToMusicRequestFields(JObject requestBody, string prompt, string lyrics, bool isInstrumental, bool lyricsOptimizer) {
            var trimmedPrompt = prompt?.Trim();
            var trimmedLyrics = lyrics?.Trim();

            if (isInstrumental || ( lyricsOptimizer && string.IsNullOrWhiteSpace(trimmedLyrics) )) {
                ValidateTextLength(trimmedPrompt, "prompt", 1, 2000);
            } else if (!string.IsNullOrWhiteSpace(trimmedPrompt)) {
                ValidateTextLength(trimmedPrompt, "prompt", 0, 2000);
            }

            if (!isInstrumental && !lyricsOptimizer && string.IsNullOrWhiteSpace(trimmedLyrics)) {
                throw new ArgumentException("Lyrics are required for non-instrumental music-2.6 generation unless lyricsOptimizer is true.", nameof(lyrics));
            }

            if (!string.IsNullOrWhiteSpace(trimmedLyrics)) {
                ValidateTextLength(trimmedLyrics, "lyrics", 1, 3500);
                requestBody["lyrics"] = trimmedLyrics;
            }

            if (!string.IsNullOrWhiteSpace(trimmedPrompt)) {
                requestBody["prompt"] = trimmedPrompt;
            }

            requestBody["lyrics_optimizer"] = lyricsOptimizer;
            requestBody["is_instrumental"] = isInstrumental;
        }

        private static void AppendCoverRequestFields(
            JObject requestBody,
            string prompt,
            string lyrics,
            string audioUrl,
            string audioBase64,
            string coverFeatureId,
            bool isInstrumental,
            bool lyricsOptimizer) {
            if (isInstrumental) {
                throw new ArgumentException("isInstrumental is not supported by music-cover models.", nameof(isInstrumental));
            }

            if (lyricsOptimizer) {
                throw new ArgumentException("lyricsOptimizer is not supported by music-cover models.", nameof(lyricsOptimizer));
            }

            var trimmedPrompt = prompt?.Trim();
            ValidateTextLength(trimmedPrompt, "prompt", 10, 300);
            requestBody["prompt"] = trimmedPrompt;

            var sources = new[] {
                !string.IsNullOrWhiteSpace(audioUrl),
                !string.IsNullOrWhiteSpace(audioBase64),
                !string.IsNullOrWhiteSpace(coverFeatureId)
            }.Count(x => x);
            if (sources != 1) {
                throw new ArgumentException("For music-cover models, provide exactly one of audioUrl, audioBase64, or coverFeatureId.");
            }

            var trimmedLyrics = lyrics?.Trim();
            if (!string.IsNullOrWhiteSpace(coverFeatureId)) {
                ValidateTextLength(trimmedLyrics, "lyrics", 10, 1000);
                requestBody["cover_feature_id"] = coverFeatureId.Trim();
                requestBody["lyrics"] = trimmedLyrics;
                return;
            }

            if (!string.IsNullOrWhiteSpace(audioUrl)) {
                requestBody["audio_url"] = audioUrl.Trim();
            } else {
                requestBody["audio_base64"] = audioBase64.Trim();
            }

            if (!string.IsNullOrWhiteSpace(trimmedLyrics)) {
                ValidateTextLength(trimmedLyrics, "lyrics", 10, 1000);
                requestBody["lyrics"] = trimmedLyrics;
            }
        }

        private static void ValidateTextLength(string value, string name, int minLength, int maxLength) {
            var length = value?.Length ?? 0;
            if (length < minLength || length > maxLength) {
                throw new ArgumentException($"{name} length must be between {minLength} and {maxLength} characters.", name);
            }
        }

        private static void ValidateAudioSetting(int sampleRate, int bitrate) {
            if (!AllowedSampleRates.Contains(sampleRate)) {
                throw new ArgumentException($"sampleRate must be one of: {string.Join(", ", AllowedSampleRates)}.", nameof(sampleRate));
            }

            if (!AllowedBitrates.Contains(bitrate)) {
                throw new ArgumentException($"bitrate must be one of: {string.Join(", ", AllowedBitrates)}.", nameof(bitrate));
            }
        }

        private static bool IsMiniMaxMusicModel(string modelName) {
            return MiniMaxMusicModels.Contains(modelName?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsMiniMaxCoverModel(string modelName) {
            return modelName?.Trim().Equals("music-cover", StringComparison.OrdinalIgnoreCase) == true ||
                   modelName?.Trim().Equals("music-cover-free", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static string NormalizeChoice(string value, string parameterName, string defaultValue, params string[] allowed) {
            var normalized = string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim().ToLowerInvariant();
            if (!allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase)) {
                throw new ArgumentException($"{parameterName} must be one of: {string.Join(", ", allowed)}.");
            }

            return normalized;
        }

        private static string ExtractErrorMessage(string responseText) {
            if (string.IsNullOrWhiteSpace(responseText)) {
                return "empty response body";
            }

            try {
                var token = JToken.Parse(responseText);
                var message = token.SelectToken("$.error.message")?.Value<string>()
                    ?? token.SelectToken("$.base_resp.status_msg")?.Value<string>()
                    ?? token.SelectToken("$.message")?.Value<string>();
                if (!string.IsNullOrWhiteSpace(message)) {
                    return message;
                }
            } catch {
                // fall through to truncated raw body
            }

            return responseText.Length <= 2000 ? responseText : responseText.Substring(0, 2000);
        }

        private static bool LooksLikeHex(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return false;
            }

            var normalized = NormalizeHex(value);
            return normalized.Length > 0 &&
                   normalized.Length % 2 == 0 &&
                   Regex.IsMatch(normalized, "^[0-9a-fA-F]+$", RegexOptions.CultureInvariant);
        }

        private static byte[] HexToBytes(string value) {
            var normalized = NormalizeHex(value);
            if (normalized.Length == 0 || normalized.Length % 2 != 0 || !Regex.IsMatch(normalized, "^[0-9a-fA-F]+$", RegexOptions.CultureInvariant)) {
                throw new InvalidOperationException("MiniMax music API returned invalid hex audio data.");
            }

            var bytes = new byte[normalized.Length / 2];
            for (var i = 0; i < bytes.Length; i++) {
                bytes[i] = Convert.ToByte(normalized.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

        private static string NormalizeHex(string value) {
            var normalized = Regex.Replace(value.Trim(), @"\s+", string.Empty);
            return normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? normalized.Substring(2) : normalized;
        }

        private static bool IsHttpUrl(string value) {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                   ( uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                     uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) );
        }

        private static bool IsTelegramAudioContentType(string contentType) {
            var normalized = contentType?.Trim().ToLowerInvariant();
            return normalized == "audio/mpeg" ||
                   normalized == "audio/mp3" ||
                   normalized == "audio/mp4" ||
                   normalized == "audio/x-m4a";
        }

        private static string ContentTypeFromAudioFormat(string format) {
            return format?.Trim().ToLowerInvariant() switch {
                "wav" => "audio/wav",
                "pcm" => "audio/L16",
                _ => "audio/mpeg"
            };
        }

        private static string ExtensionFromAudioFormat(string format) {
            return format?.Trim().ToLowerInvariant() switch {
                "wav" => ".wav",
                "pcm" => ".pcm",
                _ => ".mp3"
            };
        }

        private static string ExtensionFromContentType(string contentType) {
            return contentType?.Trim().ToLowerInvariant() switch {
                "audio/mpeg" or "audio/mp3" => ".mp3",
                "audio/wav" or "audio/wave" or "audio/x-wav" => ".wav",
                "audio/l16" or "audio/pcm" => ".pcm",
                _ => null
            };
        }

        private static string ExtensionFromUrl(string url) {
            try {
                var extension = Path.GetExtension(new Uri(url).AbsolutePath);
                return extension?.ToLowerInvariant() switch {
                    ".mp3" => ".mp3",
                    ".wav" or ".wave" => ".wav",
                    ".pcm" => ".pcm",
                    _ => null
                };
            } catch {
                return null;
            }
        }

        internal sealed record MusicResponseData(
            byte[] Bytes,
            string Url,
            int? DurationMilliseconds,
            int? SampleRate,
            int? Channels,
            int? Bitrate,
            string RequestedFormat);

        private sealed record GeneratedMusicPayload(byte[] Bytes, GeneratedMusicInfo Info);
    }
}

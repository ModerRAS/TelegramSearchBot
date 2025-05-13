using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Interfaces; 
using TelegramSearchBot.Manager;  
using TelegramSearchBot.Model;    
using System.Net.Http; // Added for HttpClient
using Microsoft.Extensions.Logging; // Added for ILogger

namespace TelegramSearchBot.Grains
{
    public class AsrGrain : Grain, IAsrGrain, IAsyncObserver<StreamMessage<Message>>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly WhisperManager _whisperManager;
        private readonly IAudioProcessingService _audioProcessingService;
        private readonly Microsoft.Extensions.Logging.ILogger<AsrGrain> _logger; // Changed type
        private readonly IGrainFactory _grainFactory;
        private readonly IHttpClientFactory _httpClientFactory; // Added

        private IAsyncStream<StreamMessage<Message>> _rawAudioStream;
        private IAsyncStream<StreamMessage<string>> _textContentStream;

        public AsrGrain(
            ITelegramBotClient botClient, 
            WhisperManager whisperManager, 
            IAudioProcessingService audioProcessingService, 
            IGrainFactory grainFactory,
            IHttpClientFactory httpClientFactory, // Added
            Microsoft.Extensions.Logging.ILogger<AsrGrain> logger) // Added
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _whisperManager = whisperManager ?? throw new ArgumentNullException(nameof(whisperManager));
            _audioProcessingService = audioProcessingService ?? throw new ArgumentNullException(nameof(audioProcessingService));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)); // Added
            _logger = logger ?? throw new ArgumentNullException(nameof(logger)); // Changed
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AsrGrain {GrainId} activated.", this.GetGrainId()); // Changed to LogInformation

            var streamProvider = this.GetStreamProvider("DefaultSMSProvider"); 

            _rawAudioStream = streamProvider.GetStream<StreamMessage<Message>>(
                OrleansStreamConstants.RawAudioMessagesStreamName,
                OrleansStreamConstants.RawMessagesStreamNamespace);
            await _rawAudioStream.SubscribeAsync(this);

            _textContentStream = streamProvider.GetStream<StreamMessage<string>>(
                OrleansStreamConstants.TextContentToProcessStreamName,
                OrleansStreamConstants.TextContentStreamNamespace);

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task OnNextAsync(StreamMessage<Message> streamMessage, StreamSequenceToken token = null)
        {
            var originalMessage = streamMessage.Payload;
            // 支持音频、语音、视频
            bool isAudio = originalMessage?.Audio != null || originalMessage?.Voice != null;
            bool isVideo = originalMessage?.Video != null;
            if (!isAudio && !isVideo)
            {
                _logger.LogWarning("AsrGrain received message without audio/voice/video data. MessageId: {MessageId}", originalMessage?.MessageId);
                return;
            }

            _logger.LogInformation("AsrGrain received audio/voice/video message. ChatId: {ChatId}, MessageId: {MessageId}",
                originalMessage.Chat.Id, originalMessage.MessageId);

            string fileId = originalMessage.Audio?.FileId ?? originalMessage.Voice?.FileId ?? originalMessage.Video?.FileId;
            string fileName = originalMessage.Audio?.FileName ?? originalMessage.Video?.FileName ?? $"{originalMessage.MessageId}_media";

            string asrResultText = null;
            string tempFilePath = null;

            try
            {
                var telegramFile = await _botClient.GetFile(fileId);
                if (telegramFile.FilePath == null)
                {
                    _logger.LogError("Unable to get file path for FileId {FileId} from Telegram for ASR.", fileId);
                    throw new Exception($"Telegram API did not return a file path for FileId {fileId} (ASR).");
                }
                string originalExtension = Path.GetExtension(telegramFile.FilePath);
                if (string.IsNullOrEmpty(originalExtension) && originalMessage.Voice != null) originalExtension = ".ogg";
                tempFilePath = Path.Combine(Path.GetTempPath(), telegramFile.FileUniqueId + originalExtension);

                await using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
                {
                    var httpClient = _httpClientFactory.CreateClient();
                    var fileUrl = $"https://api.telegram.org/file/bot{TelegramSearchBot.Env.BotToken}/{telegramFile.FilePath}";
                    _logger.LogInformation("Attempting to download media file from URL: {FileUrl}", fileUrl);
                    using var response = await httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
                    response.EnsureSuccessStatusCode();
                    await using var contentStream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
                    await contentStream.CopyToAsync(fileStream, CancellationToken.None);
                }
                _logger.LogInformation("Media downloaded to {TempFilePath} for ASR.", tempFilePath);

                // 音频/视频统一转WAV
                byte[] wavBytes = await _audioProcessingService.ConvertToWavAsync(tempFilePath);
                using (var memoryStream = new MemoryStream(wavBytes))
                {
                    asrResultText = await _whisperManager.DetectAsync(memoryStream);
                }
                if (!string.IsNullOrWhiteSpace(asrResultText))
                {
                    _logger.LogInformation("ASR successful for MessageId {MessageId}. Text found.", originalMessage.MessageId);
                }
                else
                {
                    _logger.LogInformation("ASR for MessageId {MessageId} found no text or an issue occurred.", originalMessage.MessageId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ASR processing for MessageId {MessageId}", originalMessage.MessageId);
                var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                await senderGrain.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = originalMessage.Chat.Id,
                    Text = "语音转文字处理时发生内部错误，请稍后再试。",
                    ReplyToMessageId = originalMessage.MessageId
                });
                return;
            }
            finally
            {
                if (tempFilePath != null && System.IO.File.Exists(tempFilePath))
                {
                    try { System.IO.File.Delete(tempFilePath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp ASR file: {TempFilePath}", tempFilePath); }
                }
            }

            if (!string.IsNullOrWhiteSpace(asrResultText))
            {
                var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                const int telegramMessageLengthLimit = 4000;
                if (asrResultText.Length > telegramMessageLengthLimit)
                {
                    _logger.LogInformation("ASR text for MessageId {MessageId} is too long ({Length}), sending as SRT file.", originalMessage.MessageId, asrResultText.Length);
                    try
                    {
                        var srtFileName = $"{originalMessage.MessageId}.srt";
                        var srtContent = $"1\n00:00:00,000 --> 00:01:00,000\n{asrResultText}\n";
                        var srtBytes = System.Text.Encoding.UTF8.GetBytes(srtContent);
                        await senderGrain.SendDocumentAsync(
                            originalMessage.Chat.Id,
                            srtBytes,
                            srtFileName,
                            "语音转文字结果 (SRT格式)",
                            originalMessage.MessageId
                        );
                    }
                    catch (Exception docEx)
                    {
                        _logger.LogError(docEx, "Failed to send SRT file for MessageId {MessageId}", originalMessage.MessageId);
                    }
                }
                else
                {
                    await senderGrain.SendMessageAsync(new TelegramMessageToSend
                    {
                        ChatId = originalMessage.Chat.Id,
                        Text = asrResultText,
                        ReplyToMessageId = originalMessage.MessageId
                    });
                }
                // 2. Publish ASR text to the text processing stream for further internal processing
                var textContentMessage = new StreamMessage<string>(
                    payload: asrResultText,
                    originalMessageId: originalMessage.MessageId,
                    chatId: originalMessage.Chat.Id,
                    userId: originalMessage.From?.Id ?? 0,
                    source: "AsrGrainResult"
                );
                await _textContentStream.OnNextAsync(textContentMessage);
                _logger.LogInformation("ASR result for MessageId {MessageId} published to TextContentToProcess stream.", originalMessage.MessageId);
            }
        }

        public Task OnCompletedAsync()
        {
            _logger.LogInformation("AsrGrain {GrainId} completed stream processing.", this.GetGrainId()); // Changed to LogInformation
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.LogError(ex, "AsrGrain {GrainId} encountered an error on stream.", this.GetGrainId()); // Changed to LogError
            return Task.CompletedTask;
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.LogInformation("AsrGrain {GrainId} deactivating. Reason: {Reason}", this.GetGrainId(), reason); // Changed to LogInformation
            if (_rawAudioStream != null)
            {
                var subscriptions = await _rawAudioStream.GetAllSubscriptionHandles();
                foreach (var sub in subscriptions)
                {
                    await sub.UnsubscribeAsync();
                }
            }
            await base.OnDeactivateAsync(reason, cancellationToken);
        }
    }
}

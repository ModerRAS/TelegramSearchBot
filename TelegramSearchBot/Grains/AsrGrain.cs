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
using TelegramSearchBot.Interfaces; // For IAsrGrain, ITelegramMessageSenderGrain, IAudioProcessingService
using TelegramSearchBot.Manager;  // For WhisperManager
using TelegramSearchBot.Model;    // For StreamMessage, OrleansStreamConstants
// Removed: using TelegramSearchBot.Intrerface; 

namespace TelegramSearchBot.Grains
{
    public class AsrGrain : Grain, IAsrGrain, IAsyncObserver<StreamMessage<Message>>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly WhisperManager _whisperManager;
        private readonly IAudioProcessingService _audioProcessingService;
        private readonly ILogger _logger;
        private readonly IGrainFactory _grainFactory;

        private IAsyncStream<StreamMessage<Message>> _rawAudioStream;
        private IAsyncStream<StreamMessage<string>> _textContentStream;

        public AsrGrain(ITelegramBotClient botClient, WhisperManager whisperManager, IAudioProcessingService audioProcessingService, IGrainFactory grainFactory)
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _whisperManager = whisperManager ?? throw new ArgumentNullException(nameof(whisperManager));
            _audioProcessingService = audioProcessingService ?? throw new ArgumentNullException(nameof(audioProcessingService));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _logger = Log.ForContext<AsrGrain>();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("AsrGrain {GrainId} activated.", this.GetGrainId());

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
            if (originalMessage?.Audio == null && originalMessage?.Voice == null)
            {
                _logger.Warning("AsrGrain received message without audio/voice data. MessageId: {MessageId}", originalMessage?.MessageId);
                return;
            }

            _logger.Information("AsrGrain received audio/voice message. ChatId: {ChatId}, MessageId: {MessageId}",
                originalMessage.Chat.Id, originalMessage.MessageId);

            string fileId = originalMessage.Audio?.FileId ?? originalMessage.Voice?.FileId;
            string fileName = originalMessage.Audio?.FileName ?? $"{originalMessage.MessageId}_voice.ogg"; // Default for voice

            string asrResultText = null;
            string tempFilePath = null;
            string tempWavPath = null; // If ConvertToWav saves to a new path

            try
            {
                var fileInfo = await _botClient.GetFileAsync(fileId); // Ensure GetFileAsync is used
                if (fileInfo.FilePath == null)
                {
                    _logger.Error("Unable to get file path for FileId {FileId} from Telegram for ASR.", fileId);
                    throw new Exception($"Telegram API did not return a file path for FileId {fileId} (ASR).");
                }
                
                // Ensure a unique name for the initially downloaded file
                string originalExtension = Path.GetExtension(fileInfo.FilePath);
                if (string.IsNullOrEmpty(originalExtension) && originalMessage.Voice != null) originalExtension = ".ogg"; // Voice often doesn't have extension in FilePath
                tempFilePath = Path.Combine(Path.GetTempPath(), fileInfo.FileUniqueId + originalExtension);

                using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await _botClient.DownloadFile(fileInfo.FilePath, fileStream);
                }
                _logger.Information("Audio downloaded to {TempFilePath} for ASR.", tempFilePath);

                // Convert to WAV using the injected service
                byte[] wavBytes = await _audioProcessingService.ConvertToWavAsync(tempFilePath);
                
                using (var memoryStream = new MemoryStream(wavBytes))
                {
                    // Perform ASR using WhisperManager.DetectAsync(Stream)
                    asrResultText = await _whisperManager.DetectAsync(memoryStream);
                }
                
                if (!string.IsNullOrWhiteSpace(asrResultText))
                {
                    _logger.Information("ASR successful for MessageId {MessageId}. Text found.", originalMessage.MessageId);
                }
                else
                {
                    _logger.Information("ASR for MessageId {MessageId} found no text or an issue occurred.", originalMessage.MessageId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during ASR processing for MessageId {MessageId}", originalMessage.MessageId);
                var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                await senderGrain.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = originalMessage.Chat.Id,
                    Text = $"语音转文字处理时出错: {ex.Message}",
                    ReplyToMessageId = originalMessage.MessageId
                });
                return;
            }
            finally
            {
                if (tempFilePath != null && System.IO.File.Exists(tempFilePath))
                {
                    try { System.IO.File.Delete(tempFilePath); } catch (Exception ex) { _logger.Warning(ex, "Failed to delete temp ASR file: {TempFilePath}", tempFilePath); }
                }
                // If ConvertToWav created another temp file, delete it too.
                // For now, assuming ConvertToWav(string path) returns bytes directly or uses the same temp path.
            }

            if (!string.IsNullOrWhiteSpace(asrResultText))
            {
                var textContentMessage = new StreamMessage<string>(
                    payload: asrResultText,
                    originalMessageId: originalMessage.MessageId,
                    chatId: originalMessage.Chat.Id,
                    userId: originalMessage.From?.Id ?? 0,
                    source: "AsrGrainResult"
                );
                await _textContentStream.OnNextAsync(textContentMessage);
                _logger.Information("ASR result for MessageId {MessageId} published to TextContentToProcess stream.", originalMessage.MessageId);
            }
        }

        public Task OnCompletedAsync()
        {
            _logger.Information("AsrGrain {GrainId} completed stream processing.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.Error(ex, "AsrGrain {GrainId} encountered an error on stream.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.Information("AsrGrain {GrainId} deactivating. Reason: {Reason}", this.GetGrainId(), reason);
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

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
using TelegramSearchBot.Manager; // For QRManager
using TelegramSearchBot.Model;   // For StreamMessage, OrleansStreamConstants

namespace TelegramSearchBot.Grains
{
    public class QrCodeScanGrain : Grain, IQrCodeScanGrain, IAsyncObserver<StreamMessage<Message>>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly QRManager _qrManager;
        private readonly ILogger _logger;
        private readonly IGrainFactory _grainFactory;

        private IAsyncStream<StreamMessage<Message>> _rawImageStream;
        private IAsyncStream<StreamMessage<string>> _textContentStream;

        public QrCodeScanGrain(ITelegramBotClient botClient, QRManager qrManager, IGrainFactory grainFactory)
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _qrManager = qrManager ?? throw new ArgumentNullException(nameof(qrManager));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _logger = Log.ForContext<QrCodeScanGrain>();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("QrCodeScanGrain {GrainId} activated.", this.GetGrainId());

            var streamProvider = this.GetStreamProvider("DefaultSMSProvider"); // Assuming "DefaultSMSProvider"

            _rawImageStream = streamProvider.GetStream<StreamMessage<Message>>(
                OrleansStreamConstants.RawImageMessagesStreamName,
                OrleansStreamConstants.RawMessagesStreamNamespace);
            await _rawImageStream.SubscribeAsync(this);

            _textContentStream = streamProvider.GetStream<StreamMessage<string>>(
                OrleansStreamConstants.TextContentToProcessStreamName,
                OrleansStreamConstants.TextContentStreamNamespace);

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task OnNextAsync(StreamMessage<Message> streamMessage, StreamSequenceToken token = null)
        {
            var originalMessage = streamMessage.Payload;
            if (originalMessage?.Photo == null || !originalMessage.Photo.Any())
            {
                _logger.Warning("QrCodeScanGrain received message without photo data. MessageId: {MessageId}", originalMessage?.MessageId);
                return;
            }

            _logger.Information("QrCodeScanGrain received image message. ChatId: {ChatId}, MessageId: {MessageId}",
                originalMessage.Chat.Id, originalMessage.MessageId);

            var photoSize = originalMessage.Photo.OrderByDescending(p => p.FileSize).First();
            string qrResultText = null;
            string tempFilePath = null;

            try
            {
                var fileInfo = await _botClient.GetFile(photoSize.FileId); // Use GetFile
                if (fileInfo.FilePath == null)
                {
                    _logger.Error("Unable to get file path for FileId {FileId} from Telegram for QR scan.", photoSize.FileId);
                    throw new Exception($"Telegram API did not return a file path for FileId {photoSize.FileId} (QR scan).");
                }
                tempFilePath = Path.Combine(Path.GetTempPath(), fileInfo.FileUniqueId + Path.GetExtension(fileInfo.FilePath));
                
                using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await _botClient.DownloadFile(fileInfo.FilePath, fileStream); // Use DownloadFile
                }
                _logger.Information("Photo downloaded to {TempFilePath} for QR scan.", tempFilePath);

                // Perform QR Scan using QRManager.ExecuteAsync(filePath)
                qrResultText = await _qrManager.ExecuteAsync(tempFilePath); 
                
                if (!string.IsNullOrWhiteSpace(qrResultText))
                {
                    _logger.Information("QR scan successful for MessageId {MessageId}. Text found: {FoundText}", originalMessage.MessageId, qrResultText);
                }
                else
                {
                    _logger.Information("QR scan for MessageId {MessageId} found no text or an issue occurred.", originalMessage.MessageId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during QR scan processing for MessageId {MessageId}", originalMessage.MessageId);
                var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                await senderGrain.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = originalMessage.Chat.Id,
                    Text = $"二维码扫描处理图片时出错: {ex.Message}",
                    ReplyToMessageId = originalMessage.MessageId
                });
                return;
            }
            finally
            {
                if (tempFilePath != null && System.IO.File.Exists(tempFilePath))
                {
                    try { System.IO.File.Delete(tempFilePath); } catch (Exception ex) { _logger.Warning(ex, "Failed to delete temp QR file: {TempFilePath}", tempFilePath); }
                }
            }

            if (!string.IsNullOrWhiteSpace(qrResultText))
            {
                var textContentMessage = new StreamMessage<string>(
                    payload: qrResultText,
                    originalMessageId: originalMessage.MessageId,
                    chatId: originalMessage.Chat.Id,
                    userId: originalMessage.From?.Id ?? 0,
                    source: "QrCodeScanGrainResult"
                );
                await _textContentStream.OnNextAsync(textContentMessage);
                _logger.Information("QR scan result for MessageId {MessageId} published to TextContentToProcess stream.", originalMessage.MessageId);
            }
        }

        public Task OnCompletedAsync()
        {
            _logger.Information("QrCodeScanGrain {GrainId} completed stream processing.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.Error(ex, "QrCodeScanGrain {GrainId} encountered an error on stream.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.Information("QrCodeScanGrain {GrainId} deactivating. Reason: {Reason}", this.GetGrainId(), reason);
            if (_rawImageStream != null)
            {
                var subscriptions = await _rawImageStream.GetAllSubscriptionHandles();
                foreach (var sub in subscriptions)
                {
                    await sub.UnsubscribeAsync();
                }
            }
            await base.OnDeactivateAsync(reason, cancellationToken);
        }
    }
}

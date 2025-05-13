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
using System.Net.Http; // Added for HttpClient
using Microsoft.Extensions.Logging; // Added for ILogger

namespace TelegramSearchBot.Grains
{
    public class QrCodeScanGrain : Grain, IQrCodeScanGrain, IAsyncObserver<StreamMessage<Message>>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly QRManager _qrManager;
        private readonly Microsoft.Extensions.Logging.ILogger<QrCodeScanGrain> _logger; // Changed type
        private readonly IGrainFactory _grainFactory;
        private readonly IHttpClientFactory _httpClientFactory; // Added

        private IAsyncStream<StreamMessage<Message>> _rawImageStream;
        private IAsyncStream<StreamMessage<string>> _textContentStream;

        public QrCodeScanGrain(
            ITelegramBotClient botClient, 
            QRManager qrManager, 
            IGrainFactory grainFactory,
            IHttpClientFactory httpClientFactory, // Added
            Microsoft.Extensions.Logging.ILogger<QrCodeScanGrain> logger) // Added
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _qrManager = qrManager ?? throw new ArgumentNullException(nameof(qrManager));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)); // Added
            _logger = logger ?? throw new ArgumentNullException(nameof(logger)); // Changed
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("QrCodeScanGrain {GrainId} activated.", this.GetGrainId()); // Changed to LogInformation

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
                _logger.LogWarning("QrCodeScanGrain received message without photo data. MessageId: {MessageId}", originalMessage?.MessageId); // Changed to LogWarning
                return;
            }

            _logger.LogInformation("QrCodeScanGrain received image message. ChatId: {ChatId}, MessageId: {MessageId}", // Changed to LogInformation
                originalMessage.Chat.Id, originalMessage.MessageId);

            var photoSize = originalMessage.Photo.OrderByDescending(p => p.FileSize).First();
            string qrResultText = null;
            string tempFilePath = null;
            string photoDir = Path.Combine(Env.WorkDir, "Photos", originalMessage.Chat.Id.ToString());

            try
            {
                // 确保Photo目录存在
                if (!Directory.Exists(photoDir))
                {
                    Directory.CreateDirectory(photoDir);
                    _logger.LogInformation("已创建图片目录: {PhotoDir}", photoDir);
                }
                var fileInfo = await _botClient.GetFile(photoSize.FileId, CancellationToken.None);
                if (fileInfo.FilePath == null)
                {
                    _logger.LogError("Unable to get file path for FileId {FileId} from Telegram for QR scan.", photoSize.FileId);
                    throw new Exception($"Telegram API did not return a file path for FileId {photoSize.FileId} (QR scan).");
                }
                tempFilePath = Path.Combine(photoDir, fileInfo.FileUniqueId + Path.GetExtension(fileInfo.FilePath));
                await using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
                {
                    var httpClient = _httpClientFactory.CreateClient();
                    var fileUrl = $"https://api.telegram.org/file/bot{TelegramSearchBot.Env.BotToken}/{fileInfo.FilePath}";
                    _logger.LogInformation("Attempting to download image for QR scan from URL: {FileUrl}", fileUrl);
                    using var response = await httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
                    response.EnsureSuccessStatusCode();
                    await using var contentStream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
                    await contentStream.CopyToAsync(fileStream, CancellationToken.None);
                }
                _logger.LogInformation("Photo downloaded to {TempFilePath} for QR scan.", tempFilePath);

                // Perform QR Scan using QRManager.ExecuteAsync(filePath)
                qrResultText = await _qrManager.ExecuteAsync(tempFilePath); 
                
                if (!string.IsNullOrWhiteSpace(qrResultText))
                {
                    _logger.LogInformation("QR scan successful for MessageId {MessageId}. Text found: {FoundText}", originalMessage.MessageId, qrResultText); // Changed to LogInformation
                }
                else
                {
                    _logger.LogInformation("QR scan for MessageId {MessageId} found no text or an issue occurred.", originalMessage.MessageId); // Changed to LogInformation
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during QR scan processing for MessageId {MessageId}", originalMessage.MessageId);
                var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                await senderGrain.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = originalMessage.Chat.Id,
                    Text = "二维码图片下载或处理失败，请稍后重试，或联系管理员检查Photo目录权限。",
                    ReplyToMessageId = originalMessage.MessageId
                });
                return;
            }
            finally
            {
                if (tempFilePath != null && System.IO.File.Exists(tempFilePath))
                {
                    try { System.IO.File.Delete(tempFilePath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp QR file: {TempFilePath}", tempFilePath); } // Changed to LogWarning
                }
            }

            if (!string.IsNullOrWhiteSpace(qrResultText))
            {
                // 1. Reply directly to the user with the QR content (as per user guide)
                var directReplySender = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                await directReplySender.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = originalMessage.Chat.Id,
                    Text = $"识别到二维码内容：\n{qrResultText}",
                    ReplyToMessageId = originalMessage.MessageId
                });
                _logger.LogInformation("QR scan result for MessageId {MessageId} sent directly to user.", originalMessage.MessageId); // Changed to LogInformation

                // 2. Publish QR content to the text processing stream for further internal processing (e.g., URL extraction)
                var textContentMessage = new StreamMessage<string>(
                    payload: qrResultText, // Publish the raw QR text
                    originalMessageId: originalMessage.MessageId, // Keep original message context
                    chatId: originalMessage.Chat.Id,
                    userId: originalMessage.From?.Id ?? 0,
                    source: "QrCodeScanGrainResult" 
                );
                await _textContentStream.OnNextAsync(textContentMessage);
                _logger.LogInformation("QR scan result for MessageId {MessageId} published to TextContentToProcess stream.", originalMessage.MessageId); // Changed to LogInformation
            }
        }

        public Task OnCompletedAsync()
        {
            _logger.LogInformation("QrCodeScanGrain {GrainId} completed stream processing.", this.GetGrainId()); // Changed to LogInformation
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.LogError(ex, "QrCodeScanGrain {GrainId} encountered an error on stream.", this.GetGrainId()); // Changed to LogError
            return Task.CompletedTask;
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.LogInformation("QrCodeScanGrain {GrainId} deactivating. Reason: {Reason}", this.GetGrainId(), reason); // Changed to LogInformation
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

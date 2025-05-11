using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.IO;
using System.Threading; // For CancellationToken
using System.Linq;
using System.Collections.Generic; // For List<T>
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Interfaces;
using TelegramSearchBot.Manager; // For PaddleOCR
using TelegramSearchBot.Model;   // For StreamMessage, OrleansStreamConstants

namespace TelegramSearchBot.Grains
{
    // [ImplicitStreamSubscription(OrleansStreamConstants.RawImageMessagesStreamName)] // This requires the stream namespace to be part of the attribute or configured elsewhere.
    public class OcrGrain : Grain, IOcrGrain, IAsyncObserver<StreamMessage<Message>>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly PaddleOCR _ocrService;
        private readonly ILogger _logger;
        private readonly IGrainFactory _grainFactory; // To get ITelegramMessageSenderGrain

        private IAsyncStream<StreamMessage<Message>> _rawImageStream;
        private IAsyncStream<StreamMessage<string>> _textContentStream; // Assuming payload is string (OCR'd text)

        public OcrGrain(ITelegramBotClient botClient, PaddleOCR ocrService, IGrainFactory grainFactory)
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _logger = Log.ForContext<OcrGrain>();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("OcrGrain {GrainId} activated.", this.GetGrainId());

            var streamProvider = this.GetStreamProvider("DefaultSMSProvider"); // Assuming "DefaultSMSProvider"

            // Subscribe to the raw image message stream
            _rawImageStream = streamProvider.GetStream<StreamMessage<Message>>(
                OrleansStreamConstants.RawImageMessagesStreamName,
                OrleansStreamConstants.RawMessagesStreamNamespace);
            await _rawImageStream.SubscribeAsync(this);

            // Get the stream for publishing text content
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
                _logger.Warning("OcrGrain received message without photo data. MessageId: {MessageId}", originalMessage?.MessageId);
                return;
            }

            _logger.Information("OcrGrain received image message. ChatId: {ChatId}, MessageId: {MessageId}", 
                originalMessage.Chat.Id, originalMessage.MessageId);

            // Get the largest photo (last one in the array)
            var photoSize = originalMessage.Photo.OrderByDescending(p => p.FileSize).First();
            string ocrResultText = null;
            string tempFilePath = null;

            try
            {
                var fileInfo = await _botClient.GetFile(photoSize.FileId); // Changed to GetFile
                if (fileInfo.FilePath == null)
                {
                    _logger.Error("Unable to get file path for FileId {FileId} from Telegram.", photoSize.FileId);
                    throw new Exception($"Telegram API did not return a file path for FileId {photoSize.FileId}.");
                }
                tempFilePath = Path.Combine(Path.GetTempPath(), fileInfo.FileUniqueId + Path.GetExtension(fileInfo.FilePath));
                
                using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await _botClient.DownloadFile(fileInfo.FilePath, fileStream); // Changed to DownloadFile
                }
                _logger.Information("Photo downloaded to {TempFilePath} for OCR.", tempFilePath);

                // Perform OCR
                byte[] imageBytes = await System.IO.File.ReadAllBytesAsync(tempFilePath);
                string base64ImageString = Convert.ToBase64String(imageBytes);
                
                // Assuming the response structure from OCRBootstrap.cs:
                // response.Status
                // response.Results (collection of collections, inner collection has .Text)
                var ocrResponse = await Task.Run(() => _ocrService.Execute(new List<string>() { base64ImageString }));

                if (ocrResponse != null && int.TryParse(ocrResponse.Status, out int status) && status == 0 && ocrResponse.Results != null)
                {
                    var stringList = new List<string>();
                    foreach (var resultCollection in ocrResponse.Results)
                    {
                        if (resultCollection == null) continue;
                        foreach (var textResult in resultCollection)
                        {
                            if (textResult != null && !string.IsNullOrEmpty(textResult.Text))
                            {
                                stringList.Add(textResult.Text);
                            }
                        }
                    }
                    if (stringList.Any())
                    {
                        ocrResultText = string.Join("\n", stringList);
                        _logger.Information("OCR successful for MessageId {MessageId}. Text found: {FoundText}", originalMessage.MessageId, true);
                    }
                    else
                    {
                        _logger.Information("OCR for MessageId {MessageId} found no text.", originalMessage.MessageId);
                    }
                }
                else
                {
                    _logger.Warning("OCR for MessageId {MessageId} failed or returned empty. Status: {Status}", originalMessage.MessageId, ocrResponse?.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during OCR processing for MessageId {MessageId}", originalMessage.MessageId);
                var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0); // Assuming stateless worker or well-known ID
                await senderGrain.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = originalMessage.Chat.Id,
                    Text = $"OCR处理图片时出错: {ex.Message}",
                    ReplyToMessageId = originalMessage.MessageId
                });
                return; // Stop processing this message
            }
            finally
            {
                if (tempFilePath != null && System.IO.File.Exists(tempFilePath))
                {
                    try { System.IO.File.Delete(tempFilePath); } catch (Exception ex) { _logger.Warning(ex, "Failed to delete temp OCR file: {TempFilePath}", tempFilePath); }
                }
            }

            if (!string.IsNullOrWhiteSpace(ocrResultText))
            {
                var textContentMessage = new StreamMessage<string>(
                    payload: ocrResultText,
                    originalMessageId: originalMessage.MessageId,
                    chatId: originalMessage.Chat.Id,
                    userId: originalMessage.From?.Id ?? 0,
                    source: "OcrGrainResult"
                );
                await _textContentStream.OnNextAsync(textContentMessage);
                _logger.Information("OCR result for MessageId {MessageId} published to TextContentToProcess stream.", originalMessage.MessageId);
            }
        }

        public Task OnCompletedAsync()
        {
            _logger.Information("OcrGrain {GrainId} completed stream processing.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.Error(ex, "OcrGrain {GrainId} encountered an error on stream.", this.GetGrainId());
            return Task.CompletedTask;
        }
        
        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.Information("OcrGrain {GrainId} deactivating. Reason: {Reason}", this.GetGrainId(), reason);
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

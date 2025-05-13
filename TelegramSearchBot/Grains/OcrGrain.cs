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
using System.Net.Http; // Added for HttpClient
using Microsoft.Extensions.Logging; // Added for ILogger extension methods

namespace TelegramSearchBot.Grains
{
    // [ImplicitStreamSubscription(OrleansStreamConstants.RawImageMessagesStreamName)] // This requires the stream namespace to be part of the attribute or configured elsewhere.
    public class OcrGrain : Grain, IOcrGrain, IAsyncObserver<StreamMessage<Message>>
    {
        private readonly ITelegramBotClient _botClient;
        private readonly PaddleOCR _ocrService;
        private readonly Microsoft.Extensions.Logging.ILogger<OcrGrain> _logger; // Changed type
        private readonly IGrainFactory _grainFactory; 
        private readonly IHttpClientFactory _httpClientFactory;

        private IAsyncStream<StreamMessage<Message>> _rawImageStream;
        private IAsyncStream<StreamMessage<string>> _textContentStream; 

        public OcrGrain(
            ITelegramBotClient botClient, 
            PaddleOCR ocrService, 
            IGrainFactory grainFactory,
            IHttpClientFactory httpClientFactory,
            Microsoft.Extensions.Logging.ILogger<OcrGrain> logger) // Added logger injection
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger)); // Use injected logger
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("OcrGrain {GrainId} activated.", this.GetGrainId());

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
                _logger.LogWarning("OcrGrain received message without photo data. MessageId: {MessageId}", originalMessage?.MessageId);
                return;
            }

            _logger.LogInformation("OcrGrain received image message. ChatId: {ChatId}, MessageId: {MessageId}", 
                originalMessage.Chat.Id, originalMessage.MessageId);

            // Get the largest photo (last one in the array)
            var photoSize = originalMessage.Photo.OrderByDescending(p => p.FileSize).First();
            string ocrResultText = null;
            string tempFilePath = null;

            try
            {
                var fileInfo = await _botClient.GetFile(photoSize.FileId);
                if (fileInfo.FilePath == null)
                {
                    _logger.LogError("Unable to get file path for FileId {FileId} from Telegram.", photoSize.FileId);
                    throw new Exception($"Telegram API did not return a file path for FileId {photoSize.FileId}.");
                }
                tempFilePath = Path.Combine(Path.GetTempPath(), fileInfo.FileUniqueId + Path.GetExtension(fileInfo.FilePath));
                
                await using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
                {
                    // Workaround: Use HttpClient to download the file directly
                    var httpClient = _httpClientFactory.CreateClient();
                    // Env.BotToken needs to be accessible here. Assuming it's a static property.
                    var fileUrl = $"https://api.telegram.org/file/bot{TelegramSearchBot.Env.BotToken}/{fileInfo.FilePath}"; // Used fully qualified Env
                    
                    _logger.LogInformation("Attempting to download file from URL: {FileUrl}", fileUrl);

                    using var response = await httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
                    response.EnsureSuccessStatusCode();
                    await using var contentStream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
                    await contentStream.CopyToAsync(fileStream, CancellationToken.None);
                }
                _logger.LogInformation("Photo downloaded to {TempFilePath} for OCR.", tempFilePath);

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
                        _logger.LogInformation("OCR successful for MessageId {MessageId}. Text found: {FoundText}", originalMessage.MessageId, true);
                    }
                    else
                    {
                        _logger.LogInformation("OCR for MessageId {MessageId} found no text.", originalMessage.MessageId);
                    }
                }
                else
                {
                    _logger.LogWarning("OCR for MessageId {MessageId} failed or returned empty. Status: {Status}", originalMessage.MessageId, ocrResponse?.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during OCR processing for MessageId {MessageId}", originalMessage.MessageId);
                var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0); 
                await senderGrain.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = originalMessage.Chat.Id,
                    Text = "OCR处理图片时发生内部错误，请稍后再试。", // Generic error message
                    ReplyToMessageId = originalMessage.MessageId
                });
                return; // Stop processing this message
            }
            finally
            {
                if (tempFilePath != null && System.IO.File.Exists(tempFilePath))
                {
                    try { System.IO.File.Delete(tempFilePath); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete temp OCR file: {TempFilePath}", tempFilePath); }
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
                _logger.LogInformation("OCR result for MessageId {MessageId} published to TextContentToProcess stream.", originalMessage.MessageId);

                // Check for "打印" caption as per user guide
                if (!string.IsNullOrEmpty(originalMessage.Caption) && originalMessage.Caption == "打印")
                {
                    _logger.LogInformation("OCR Grain: Caption \"打印\" found for MessageId {MessageId}. Replying with OCR text.", originalMessage.MessageId);
                    var directReplySender = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                    await directReplySender.SendMessageAsync(new TelegramMessageToSend
                    {
                        ChatId = originalMessage.Chat.Id,
                        Text = ocrResultText, // Send the OCR result directly
                        ReplyToMessageId = originalMessage.MessageId
                    });
                }
            }
        }

        public Task OnCompletedAsync()
        {
            _logger.LogInformation("OcrGrain {GrainId} completed stream processing.", this.GetGrainId());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.LogError(ex, "OcrGrain {GrainId} encountered an error on stream.", this.GetGrainId());
            return Task.CompletedTask;
        }
        
        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.LogInformation("OcrGrain {GrainId} deactivating. Reason: {Reason}", this.GetGrainId(), reason);
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

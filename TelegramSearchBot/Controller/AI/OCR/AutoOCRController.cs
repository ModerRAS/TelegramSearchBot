using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Common;
using TelegramSearchBot.Common.Model.DO;
using TelegramSearchBot.Controller.Download;
using TelegramSearchBot.Controller.Storage;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.OCR;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service.AI.OCR;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Controller.AI.OCR {
    public class AutoOCRController : IOnUpdate {
        private readonly IEnumerable<IOCRService> _ocrServices;
        private readonly IAppConfigurationService _configService;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        private readonly ILogger<AutoOCRController> logger;
        private readonly ISendMessageService SendMessageService;
        private readonly MessageExtensionService MessageExtensionService;
        public AutoOCRController(
            ITelegramBotClient botClient,
            IEnumerable<IOCRService> ocrServices,
            IAppConfigurationService configService,
            SendMessage Send,
            MessageService messageService,
            ILogger<AutoOCRController> logger,
            ISendMessageService sendMessageService,
            MessageExtensionService messageExtensionService
            ) {
            this._ocrServices = ocrServices;
            this._configService = configService;
            this.messageService = messageService;
            this.botClient = botClient;
            this.Send = Send;
            this.logger = logger;
            SendMessageService = sendMessageService;
            MessageExtensionService = messageExtensionService;
        }

        public List<Type> Dependencies => new List<Type>() { typeof(DownloadPhotoController), typeof(MessageController) };

        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            if (p.BotMessageType != BotMessageType.Message) {
                return;
            }
            if (!Env.EnableAutoOCR) {
                return;
            }
            string OcrStr = string.Empty;
            try {
                var PhotoStream = await IProcessPhoto.GetPhoto(e);
                logger.LogInformation($"Get Photo File: {e.Message.Chat.Id}/{e.Message.MessageId}");

                var engine = await GetOCREngineAsync();
                var ocrService = _ocrServices.FirstOrDefault(s => s.Engine == engine)
                    ?? _ocrServices.First(s => s.Engine == OCREngine.PaddleOCR);

                logger.LogInformation($"使用OCR引擎: {engine}");
                OcrStr = await ocrService.ExecuteAsync(new MemoryStream(PhotoStream));
                if (!string.IsNullOrWhiteSpace(OcrStr)) {
                    logger.LogInformation(OcrStr);
                    await MessageExtensionService.AddOrUpdateAsync(p.MessageDataId, "OCR_Result", OcrStr);
                    p.ProcessingResults.Add($"[OCR识别结果] {OcrStr}");
                }
            } catch (Exception ex) when (
                    ex is CannotGetPhotoException ||
                    ex is DirectoryNotFoundException
                    ) {
                logger.LogInformation($"Cannot Get Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }

            if (( !string.IsNullOrEmpty(e.Message.Caption) && e.Message.Caption.Equals("打印") ) ||
                ( e.Message.ReplyToMessage != null && e.Message.Text != null && e.Message.Text.Equals("打印") )) {
                string ocrResult = OcrStr;

                if (e.Message.ReplyToMessage != null) {
                    var originalMessageId = await MessageExtensionService.GetMessageIdByMessageIdAndGroupId(
                        e.Message.ReplyToMessage.MessageId,
                        e.Message.Chat.Id);

                    if (originalMessageId.HasValue) {
                        var extensions = await MessageExtensionService.GetByMessageDataIdAsync(originalMessageId.Value);
                        var ocrExtension = extensions.FirstOrDefault(x => x.Name == "OCR_Result");
                        if (ocrExtension != null) {
                            ocrResult = ocrExtension.Value;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(ocrResult)) {
                    await SendMessageService.SendMessage(ocrResult, e.Message.Chat.Id, e.Message.MessageId);
                }
            }
        }

        private async Task<OCREngine> GetOCREngineAsync() {
            var engineStr = await _configService.GetConfigurationValueAsync(EditOCRConfService.OCREngineKey);
            if (!string.IsNullOrEmpty(engineStr) && Enum.TryParse<OCREngine>(engineStr, out var engine)) {
                return engine;
            }
            return OCREngine.PaddleOCR;
        }
    }
}

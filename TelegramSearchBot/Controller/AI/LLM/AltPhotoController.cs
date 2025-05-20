using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using TelegramSearchBot.Controller.AI.OCR;
using TelegramSearchBot.Controller.Download;
using TelegramSearchBot.Controller.Storage;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.AI.OCR;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Controller.AI.LLM {
    public class AltPhotoController : IOnUpdate {
        public List<Type> Dependencies => new List<Type>() { typeof(DownloadPhotoController), typeof(MessageController) };
        private readonly GeneralLLMService generalLLMService;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        private readonly ILogger<AutoOCRController> logger;
        private readonly SendMessageService SendMessageService;
        private readonly MessageExtensionService MessageExtensionService;
        public AltPhotoController(
            ITelegramBotClient botClient,
            GeneralLLMService generalLLMService,
            SendMessage Send,
            MessageService messageService,
            ILogger<AutoOCRController> logger,
            SendMessageService sendMessageService,
            MessageExtensionService messageExtensionService
            ) {
            this.generalLLMService = generalLLMService;
            this.messageService = messageService;
            this.botClient = botClient;
            this.Send = Send;
            this.logger = logger;
            SendMessageService = sendMessageService;
            MessageExtensionService = messageExtensionService;
        }
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
                OcrStr = await generalLLMService.AnalyzeImageAsync(PhotoStream, e.Message.Chat.Id);
                logger.LogInformation(OcrStr);
                if (!OcrStr.StartsWith("Error")) {
                    await MessageExtensionService.AddOrUpdateAsync(p.MessageDataId, "Alt_Result", OcrStr);
                }
            } catch (Exception ex) when (
                    ex is CannotGetPhotoException ||
                    ex is DirectoryNotFoundException
                    ) {
                logger.LogInformation($"Cannot Get Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }

            if ((!string.IsNullOrEmpty(e.Message.Caption) && e.Message.Caption.Equals("描述")) ||
                (e.Message.ReplyToMessage != null && e.Message.Text != null && e.Message.Text.Equals("描述"))) {
                string ocrResult = OcrStr;

                // 如果是回复消息触发打印
                if (e.Message.ReplyToMessage != null) {
                    var originalMessageId = await MessageExtensionService.GetMessageIdByMessageIdAndGroupId(
                        e.Message.ReplyToMessage.MessageId,
                        e.Message.Chat.Id);

                    if (originalMessageId.HasValue) {
                        var extensions = await MessageExtensionService.GetByMessageDataIdAsync(originalMessageId.Value);
                        var ocrExtension = extensions.FirstOrDefault(x => x.Name == "Alt_Result");
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
    }
}

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Controller.Download;
using TelegramSearchBot.Controller.Storage;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.OCR;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Controller.AI.OCR
{
    public class AutoOCRController : IOnUpdate
    {
        private readonly PaddleOCRService paddleOCRService;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        private readonly ILogger<AutoOCRController> logger;
        private readonly SendMessageService SendMessageService;
        private readonly MessageExtensionService MessageExtensionService;
        public AutoOCRController(
            ITelegramBotClient botClient,
            PaddleOCRService paddleOCRService,
            SendMessage Send,
            MessageService messageService,
            ILogger<AutoOCRController> logger,
            SendMessageService sendMessageService,
            MessageExtensionService messageExtensionService
            )
        {
            this.paddleOCRService = paddleOCRService;
            this.messageService = messageService;
            this.botClient = botClient;
            this.Send = Send;
            this.logger = logger;
            SendMessageService = sendMessageService;
            MessageExtensionService = messageExtensionService;
        }

        public List<Type> Dependencies => new List<Type>() { typeof(DownloadPhotoController), typeof(MessageController) };

        public async Task ExecuteAsync(PipelineContext p)
        {
            var e = p.Update;
            if (p.BotMessageType != BotMessageType.Message) {
                return;
            }
            if (!Env.EnableAutoOCR)
            {
                return;
            }
            string OcrStr = string.Empty;
            try
            {
                var PhotoStream = await IProcessPhoto.GetPhoto(e);
                logger.LogInformation($"Get Photo File: {e.Message.Chat.Id}/{e.Message.MessageId}");
                OcrStr = await paddleOCRService.ExecuteAsync(new MemoryStream(PhotoStream));
                if (!string.IsNullOrWhiteSpace(OcrStr)) {
                    logger.LogInformation(OcrStr);
                    await MessageExtensionService.AddOrUpdateAsync(p.MessageDataId, "OCR_Result", OcrStr);
                }
            }
            catch (Exception ex) when (
                  ex is CannotGetPhotoException ||
                  ex is DirectoryNotFoundException
                  )
            {
                logger.LogInformation($"Cannot Get Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }

            if ((!string.IsNullOrEmpty(e.Message.Caption) && e.Message.Caption.Equals("打印")) ||
                (e.Message.ReplyToMessage != null && e.Message.Text != null && e.Message.Text.Equals("打印"))) {
                string ocrResult = OcrStr;

                // 如果是回复消息触发打印
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
    }
}

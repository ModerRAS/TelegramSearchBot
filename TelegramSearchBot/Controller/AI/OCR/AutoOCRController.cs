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
using TelegramSearchBot.Interface.AI.OCR;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.OCR;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Common.Model.DO;
using System.Text;
using Newtonsoft.Json;

namespace TelegramSearchBot.Controller.AI.OCR
{
    public class AutoOCRController : IOnUpdate
    {
        private readonly IPaddleOCRService paddleOCRService;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        private readonly ILogger<AutoOCRController> logger;
        private readonly ISendMessageService SendMessageService;
        private readonly MessageExtensionService MessageExtensionService;
        public AutoOCRController(
            ITelegramBotClient botClient,
            IPaddleOCRService paddleOCRService,
            SendMessage Send,
            MessageService messageService,
            ILogger<AutoOCRController> logger,
            ISendMessageService sendMessageService,
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
            PaddleOCRResult fullOcrResult = null;
            
            try
            {
                var PhotoStream = await IProcessPhoto.GetPhoto(e);
                logger.LogInformation($"Get Photo File: {e.Message.Chat.Id}/{e.Message.MessageId}");
                
                // 统一使用带坐标的API获取完整结果
                fullOcrResult = await paddleOCRService.ExecuteWithCoordinatesAsync(new MemoryStream(PhotoStream));
                
                // 从完整结果中提取文本用于存储和显示
                if (fullOcrResult != null && fullOcrResult.Results != null) {
                    var textList = new List<string>();
                    foreach (var resultGroup in fullOcrResult.Results) {
                        foreach (var result in resultGroup) {
                            if (!string.IsNullOrWhiteSpace(result.Text)) {
                                textList.Add(result.Text);
                            }
                        }
                    }
                    OcrStr = string.Join(" ", textList);
                }
                
                if (!string.IsNullOrWhiteSpace(OcrStr)) {
                    logger.LogInformation(OcrStr);
                    await MessageExtensionService.AddOrUpdateAsync(p.MessageDataId, "OCR_Result", OcrStr);
                    p.ProcessingResults.Add($"[OCR识别结果] {OcrStr}");
                }
                
                // 将完整的OCR结果放到PipelineContext中供后续处理使用
                if (fullOcrResult != null) {
                    p.PipelineCache["OCR_FullResult"] = fullOcrResult;
                    
                    // 同时保存完整的坐标信息到扩展数据
                    var jsonResult = JsonConvert.SerializeObject(fullOcrResult);
                    await MessageExtensionService.AddOrUpdateAsync(p.MessageDataId, "OCR_Coordinates", jsonResult);
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

using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.OCR;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Handler.AI
{
    // MediatR请求对象
    public class AutoOcrRequest : IRequest<Unit>
    {
        public Update Update { get; set; }
    }

    // MediatR Handler
    public class AutoOcrRequestHandler : IRequestHandler<AutoOcrRequest, Unit>
    {
        private readonly PaddleOCRService paddleOCRService;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly ILogger<AutoOcrRequestHandler> logger;
        private readonly SendMessageService sendMessageService;
        public AutoOcrRequestHandler(
            ITelegramBotClient botClient,
            PaddleOCRService paddleOCRService,
            MessageService messageService,
            ILogger<AutoOcrRequestHandler> logger,
            SendMessageService sendMessageService
            )
        {
            this.paddleOCRService = paddleOCRService;
            this.messageService = messageService;
            this.botClient = botClient;
            this.logger = logger;
            this.sendMessageService = sendMessageService;
        }

        public async Task<Unit> Handle(AutoOcrRequest request, CancellationToken cancellationToken)
        {
            var e = request.Update;
            if (!Env.EnableAutoOCR)
            {
                return Unit.Value;
            }

            try
            {
                var PhotoStream = await IProcessPhoto.GetPhoto(e);
                logger.LogInformation($"Get Photo File: {e.Message.Chat.Id}/{e.Message.MessageId}");
                var OcrStr = await paddleOCRService.ExecuteAsync(new MemoryStream(PhotoStream));
                logger.LogInformation(OcrStr);
                await messageService.ExecuteAsync(new MessageOption
                {
                    ChatId = e.Message.Chat.Id,
                    Chat = e.Message.Chat,
                    MessageId = e.Message.MessageId,
                    UserId = e.Message.From.Id,
                    DateTime = e.Message.Date,
                    User = e.Message.From,
                    ReplyTo = e.Message.ReplyToMessage?.Id ?? 0,
                    Content = $"{e.Message?.Caption}\n{OcrStr}"
                });

                if (!string.IsNullOrEmpty(e.Message.Caption) && e.Message.Caption.Length == 2 && e.Message.Caption.Equals("打印"))
                {
                    await sendMessageService.SendMessage(OcrStr, e.Message.Chat.Id, e.Message.MessageId);
                }
            }
            catch (Exception ex) when (
                  ex is CannotGetPhotoException ||
                  ex is DirectoryNotFoundException
                  )
            {
                //logger.LogInformation($"Cannot Get Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            }
            return Unit.Value;
        }
    }
} 
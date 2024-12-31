using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Exceptions;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class AutoASRController : IOnUpdate {
        private readonly AutoASRService autoASRService;
        private readonly MessageService messageService;
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        private readonly ILogger<AutoASRController> logger;
        public AutoASRController(ITelegramBotClient botClient, AutoASRService autoASRService, SendMessage Send, MessageService messageService, ILogger<AutoASRController> logger) {
            this.autoASRService = autoASRService;
            this.messageService = messageService;
            this.botClient = botClient;
            this.Send = Send;
            this.logger = logger;
        }
        public async Task ExecuteAsync(Update e) {
            if (!Env.EnableAutoASR) {
                return;
            }

            try {
                var AudioStream = await IProcessAudio.GetAudio(e);
                logger.LogInformation($"Get Audio File: {e.Message.Chat.Id}/{e.Message.MessageId}");
                var AsrStr = await autoASRService.ExecuteAsync(new MemoryStream(AudioStream));
                logger.LogInformation(AsrStr);
                
                await messageService.ExecuteAsync(new MessageOption {
                    ChatId = e.Message.Chat.Id,
                    MessageId = e.Message.MessageId,
                    UserId = e.Message.From.Id,
                    Chat = e.Message.Chat,
                    DateTime = e.Message.Date,
                    User = e.Message.From,
                    Content = $"{e.Message?.Caption}\n{AsrStr}"
                });
                if (AsrStr.Length > 4095) {
                    await Send.AddTask(async () => {
                        var message = await botClient.SendDocumentAsync(
                        chatId: e.Message.Chat,
                        document: InputFile.FromStream(new MemoryStream(Encoding.UTF8.GetBytes(AsrStr)), $"{e.Message.MessageId}.srt")
                        replyParameters: new ReplyParameters() { MessageId = e.Message.MessageId }
                        );
                    }, e.Message.Chat.Id < 0);
                } else {
                    await Send.AddTask(async () => {
                        var message = await botClient.SendTextMessageAsync(
                        chatId: e.Message.Chat,
                        text: AsrStr,
                        replyParameters: new ReplyParameters() { MessageId = e.Message.MessageId }
                        );
                    }, e.Message.Chat.Id < 0);
                }
                
            } catch (Exception ex) when (
                  ex is CannotGetAudioException ||
                  ex is DirectoryNotFoundException
                  ) {
                //logger.LogInformation($"Cannot Get Photo: {e.Message.Chat.Id}/{e.Message.MessageId}");
            } 
        }
    }
}

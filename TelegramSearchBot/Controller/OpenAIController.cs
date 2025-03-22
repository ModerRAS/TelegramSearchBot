using Microsoft.Extensions.Logging;
using OllamaSharp.Models.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    public class OpenAIController : IOnUpdate {
        private readonly ILogger logger;
        private readonly OpenAIService service;
        private readonly SendMessage Send;
        public List<Type> Dependencies => new List<Type>();
        public ITelegramBotClient botClient { get; set; }
        public MessageService messageService { get; set; }
        public AdminService adminService { get; set; }
        public SendMessageService SendMessageService { get; set; }
        public OpenAIController(
            MessageService messageService, 
            ITelegramBotClient botClient, 
            OpenAIService openaiService, 
            SendMessage Send, 
            ILogger<OllamaController> logger, 
            AdminService adminService, 
            SendMessageService SendMessageService
            ) {
            this.logger = logger;
            this.botClient = botClient;
            service = openaiService;
            this.Send = Send;
            this.messageService = messageService;
            this.adminService = adminService;
            this.SendMessageService = SendMessageService;

        }
        public async Task ExecuteAsync(Update e) {
            if (!Env.EnableOpenAI) { 
                return;
            }
            var BotName = (await botClient.GetMe()).Username;
            if (string.IsNullOrEmpty(service.BotName)) {
                service.BotName = BotName;
            } 
            var Message = string.IsNullOrEmpty(e?.Message?.Text) ? e?.Message?.Caption : e.Message.Text;
            if (string.IsNullOrEmpty(Message)) {
                return;
            }
            if (Message.Contains(BotName)) {
                var ModelName = await service.GetModel(e.Message.Chat.Id);
                var InitialContent = $"{ModelName}初始化中。。。";
                var messages = SendMessageService.SendMessage(service.ExecAsync(Message, e.Message.Chat.Id), e.Message.Chat.Id, e.Message.MessageId, InitialContent);
                await foreach (var PerMessage in messages) {
                    await messageService.ExecuteAsync(new MessageOption() {
                        Chat = e.Message.Chat,
                        ChatId = e.Message.Chat.Id,
                        Content = PerMessage.Content,
                        DateTime = PerMessage.DateTime,
                        MessageId = PerMessage.MessageId,
                        User = e.Message.From,
                        UserId = e.Message.From.Id
                    });
                }
            }
            if (Message.StartsWith("设置模型 ") && await adminService.IsNormalAdmin(e.Message.From.Id)) { 
                var (previous, current) = await service.SetModel(Message.Substring(5), e.Message.Chat.Id);
                await Send.AddTask(async () => {
                    var sentMessage = await botClient.SendMessage(
                        chatId: e.Message.Chat.Id,
                        text: $"模型设置成功，原模型：{previous}，现模型：{current}",
                        replyParameters: new ReplyParameters() { MessageId = e.Message.MessageId }
                        );
                }, e.Message.Chat.Id < 0);
            }
        }
    }
}

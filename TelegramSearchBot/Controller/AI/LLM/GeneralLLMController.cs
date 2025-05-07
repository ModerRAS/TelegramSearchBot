using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Controller.AI.LLM
{
    public class GeneralLLMController : IOnUpdate
    {
        private readonly ILogger logger;
        private readonly OpenAIService service;
        private readonly SendMessage Send;
        public List<Type> Dependencies => new List<Type>();
        public ITelegramBotClient botClient { get; set; }
        public MessageService messageService { get; set; }
        public AdminService adminService { get; set; }
        public SendMessageService SendMessageService { get; set; }
        public GeneralLLMService GeneralLLMService { get; set; }
        public GeneralLLMController(
            MessageService messageService,
            ITelegramBotClient botClient,
            OpenAIService openaiService,
            SendMessage Send,
            ILogger<GeneralLLMController> logger,
            AdminService adminService,
            SendMessageService SendMessageService,
            GeneralLLMService generalLLMService
            )
        {
            this.logger = logger;
            this.botClient = botClient;
            service = openaiService;
            this.Send = Send;
            this.messageService = messageService;
            this.adminService = adminService;
            this.SendMessageService = SendMessageService;
            GeneralLLMService = generalLLMService;

        }
        public async Task ExecuteAsync(Update e)
        {
            if (!Env.EnableOpenAI)
            {
                return;
            }
            if (string.IsNullOrEmpty(service.BotName))
            {
                var me = await botClient.GetMe();
                Env.BotId = me.Id;
                var BotName = me.Username;
                service.BotName = BotName;
            }
            var Message = string.IsNullOrEmpty(e?.Message?.Text) ? e?.Message?.Caption : e.Message.Text;
            if (string.IsNullOrEmpty(Message))
            {
                return;
            }
            if (Message.StartsWith("设置模型 ") && await adminService.IsNormalAdmin(e.Message.From.Id))
            {
                var (previous, current) = await service.SetModel(Message.Substring(5), e.Message.Chat.Id);
                logger.LogInformation($"群{e.Message.Chat.Id}模型设置成功，原模型：{previous}，现模型：{current}。消息来源：{e.Message.MessageId}");
                await SendMessageService.SendMessage($"模型设置成功，原模型：{previous}，现模型：{current}", e.Message.Chat.Id, e.Message.MessageId);
                return;
            }
            if (Message.Contains(service.BotName))
            {
                var ModelName = await service.GetModel(e.Message.Chat.Id);
                var InitialContent = $"{ModelName}初始化中。。。";
                var messages = SendMessageService.SendMessage(GeneralLLMService.ExecAsync(new Model.Data.Message()
                {
                    Content = Message,
                    DateTime = e.Message.Date,
                    FromUserId = e.Message.From.Id,
                    GroupId = e.Message.Chat.Id,
                    MessageId = e.Message.Id,
                    ReplyToMessageId = e.Message.ReplyToMessage?.Id ?? 0,
                    Id = -1,
                }, e.Message.Chat.Id), e.Message.Chat.Id, e.Message.MessageId, InitialContent);
                await foreach (var PerMessage in messages)
                {
                    await messageService.ExecuteAsync(new MessageOption()
                    {
                        Chat = e.Message.Chat,
                        ChatId = e.Message.Chat.Id,
                        Content = PerMessage.Content,
                        DateTime = PerMessage.DateTime,
                        MessageId = PerMessage.MessageId,
                        User = await botClient.GetMe(),
                        ReplyTo = e.Message.ReplyToMessage?.Id ?? 0,
                        UserId = (await botClient.GetMe()).Id,
                    });
                }
                return;
            }
        }
    }
}

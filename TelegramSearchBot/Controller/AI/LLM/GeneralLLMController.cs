using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading; // For CancellationToken
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
            if (Message.Contains(service.BotName)) // service is OpenAIService, BotName is set on it.
            {
                // TODO: Consider getting BotName in a more generic way if GeneralLLMService is to be truly general
                // For now, this relies on OpenAIService instance's BotName being set.

                var modelName = await service.GetModel(e.Message.Chat.Id); // Still uses OpenAIService for GetModel
                var initialContentPlaceholder = $"{modelName}初始化中。。。";

                // Prepare the input message for GeneralLLMService
                var inputLlMessage = new Model.Data.Message()
                {
                    Content = Message,
                    DateTime = e.Message.Date, // This is DateTimeOffset, Model.Data.Message.DateTime is DateTime. Ensure conversion if needed.
                    FromUserId = e.Message.From.Id,
                    GroupId = e.Message.Chat.Id,
                    MessageId = e.Message.MessageId, // Original user message ID
                    ReplyToMessageId = e.Message.ReplyToMessage?.MessageId ?? 0, // ReplyToMessage is a Message object
                    Id = -1, // Placeholder, DB will assign
                };

                // Call GeneralLLMService.ExecAsync to get the stream of full markdown messages
                // Pass CancellationToken.None as IOnUpdate doesn't provide a natural CancellationToken source here.
                IAsyncEnumerable<string> fullMessageStream = GeneralLLMService.ExecAsync(inputLlMessage, e.Message.Chat.Id, CancellationToken.None);

                // Call the new SendFullMessageStream method
                List<Model.Data.Message> sentMessagesForDb = await SendMessageService.SendFullMessageStream(
                    fullMessageStream,
                    e.Message.Chat.Id,
                    e.Message.MessageId, // Reply to the original user's message
                    initialContentPlaceholder, // Corrected variable name
                    CancellationToken.None // Pass a CancellationToken here as well
                );

                // Process the list of messages returned for DB logging
                User botUser = null; // Cache bot user info
                foreach (var dbMessage in sentMessagesForDb)
                {
                    if (botUser == null)
                    {
                        botUser = await botClient.GetMe();
                    }
                    // dbMessage already contains FromUserId (bot's ID) and Content (markdown chunk)
                    // and MessageId (the ID of the Telegram message segment)
                    // and ReplyToMessageId (the ID it replied to)
                    // DateTime is also from the Telegram Message object.
                    
                    // We need to ensure messageService.ExecuteAsync can handle Model.Data.Message directly
                    // or adapt it to MessageOption.
                    // Assuming messageService.ExecuteAsync is for saving to DB.
                    // The `dbMessage` objects are already what we want to save.
                    // However, messageService.ExecuteAsync takes MessageOption.
                    
                    await messageService.ExecuteAsync(new MessageOption()
                    {
                        Chat = e.Message.Chat, // Original chat context
                        ChatId = dbMessage.GroupId, 
                        Content = dbMessage.Content, 
                        DateTime = dbMessage.DateTime, 
                        MessageId = dbMessage.MessageId, 
                        User = botUser, 
                        ReplyTo = dbMessage.ReplyToMessageId, 
                        UserId = dbMessage.FromUserId, 
                    });
                }
                return;
            }
        }
    }
}

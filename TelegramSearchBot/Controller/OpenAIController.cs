using Microsoft.Extensions.Logging;
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
        public OpenAIController(MessageService messageService, ITelegramBotClient botClient, OpenAIService openaiService, SendMessage Send, ILogger<OllamaController> logger, AdminService adminService) {
            this.logger = logger;
            this.botClient = botClient;
            service = openaiService;
            this.Send = Send;
            this.messageService = messageService;
            this.adminService = adminService;

        }
        public async Task SendMessage(Update e, string Message) {
            // 初始化一条消息，准备编辑
            var sentMessage = await botClient.SendMessage(
                chatId: e.Message.Chat.Id,
                text: "Initializing...",
                replyParameters: new ReplyParameters() { MessageId = e.Message.MessageId }
            );
            StringBuilder builder = new StringBuilder();
            var datetime = DateTime.UtcNow;
            await foreach (var PerMessage in service.ExecAsync(Message, e.Message.Chat.Id)) {
                if (builder.Length > 1900) {
                    var tmpMessageId = sentMessage.MessageId;
                    sentMessage = await botClient.SendMessage(
                        chatId: e.Message.Chat.Id,
                        text: "Initializing...",
                        replyParameters: new ReplyParameters() { MessageId = tmpMessageId }
                        );
                    builder.Clear();
                }
                builder.Append(PerMessage);
                if (DateTime.UtcNow - datetime > TimeSpan.FromSeconds(5)) {
                    datetime = DateTime.UtcNow;
                    await Send.AddTask(async () => {
                        await botClient.EditMessageText(
                            chatId: sentMessage.Chat.Id,
                            messageId: sentMessage.MessageId,
                            parseMode: Telegram.Bot.Types.Enums.ParseMode.None,
                            text: builder.ToString()
                            );
                    }, e.Message.Chat.Id < 0);
                }
            }
            await Send.AddTask(async () => {
                var message = await botClient.EditMessageText(
                    chatId: sentMessage.Chat.Id,
                    messageId: sentMessage.MessageId,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.None,
                    text: builder.ToString()
                    );
                logger.LogInformation($"Send OpenAI result success {message.MessageId} {builder.ToString()}");
                await messageService.ExecuteAsync(new MessageOption() {
                    ChatId = e.Message.Chat.Id,
                    Chat = e.Message.Chat,
                    DateTime = e.Message.Date,
                    User = e.Message.From,
                    Content = builder.ToString(),
                    MessageId = message.MessageId,
                    UserId = e.Message.From.Id
                });
            }, e.Message.Chat.Id < 0);
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
                await SendMessage(e, Message);
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

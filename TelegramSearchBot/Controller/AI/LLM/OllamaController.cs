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
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.AI.LLM;

namespace TelegramSearchBot.Controller.AI.LLM
{
    public class OllamaController : IOnUpdate
    {
        private readonly ILogger logger;
        private readonly OllamaService service;
        private readonly SendMessage Send;
        public List<Type> Dependencies => new List<Type>();
        public ITelegramBotClient botClient { get; set; }
        public OllamaController(ITelegramBotClient botClient, OllamaService ollamaService, SendMessage Send, ILogger<OllamaController> logger)
        {
            this.logger = logger;
            this.botClient = botClient;
            service = ollamaService;
            this.Send = Send;

        }
        public async Task ExecuteAsync(Update e)
        {
            if (!Env.EnableOllama)
            {
                return;
            }
            var BotName = (await botClient.GetMe()).Username;
            if (string.IsNullOrEmpty(service.BotName))
            {
                service.BotName = BotName;
            }
            var Message = string.IsNullOrEmpty(e?.Message?.Text) ? e?.Message?.Caption : e.Message.Text;
            if (string.IsNullOrEmpty(Message))
            {
                return;
            }
            if (Message.Contains(BotName))
            {
                // 初始化一条消息，准备编辑
                var sentMessage = await botClient.SendMessage(
                    chatId: e.Message.Chat.Id,
                    text: "Initializing...",
                    replyParameters: new ReplyParameters() { MessageId = e.Message.MessageId }
                );
                StringBuilder builder = new StringBuilder();
                var num = 0;
                await foreach (var PerMessage in service.ExecAsync(Message, e.Message.Chat.Id))
                {
                    if (builder.Length > 1900)
                    {
                        var tmpMessageId = sentMessage.MessageId;
                        sentMessage = await botClient.SendMessage(
                            chatId: e.Message.Chat.Id,
                            text: "Initializing...",
                            replyParameters: new ReplyParameters() { MessageId = tmpMessageId }
                            );
                        builder.Clear();
                    }
                    builder.Append(PerMessage);
                    num++;
                    if (num % 10 == 0)
                    {
                        await Send.AddTask(async () =>
                        {
                            await botClient.EditMessageText(
                                chatId: sentMessage.Chat.Id,
                                messageId: sentMessage.MessageId,
                                parseMode: Utils.IsValidMarkdown(builder.ToString()) ? Telegram.Bot.Types.Enums.ParseMode.Markdown : Telegram.Bot.Types.Enums.ParseMode.None,
                                text: builder.ToString()
                                );
                        }, e.Message.Chat.Id < 0);
                    }
                }
                await Send.AddTask(async () =>
                {
                    await botClient.EditMessageText(
                        chatId: sentMessage.Chat.Id,
                        messageId: sentMessage.MessageId,
                        parseMode: Utils.IsValidMarkdown(builder.ToString()) ? Telegram.Bot.Types.Enums.ParseMode.Markdown : Telegram.Bot.Types.Enums.ParseMode.None,
                        text: builder.ToString()
                        );
                }, e.Message.Chat.Id < 0);
            }
        }
    }
}

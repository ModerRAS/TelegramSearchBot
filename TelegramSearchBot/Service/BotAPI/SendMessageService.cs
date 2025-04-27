using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Service.BotAPI
{
    public class SendMessageService : IService
    {
        public string ServiceName => "SendMessageService";
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send;
        private readonly ILogger<SendMessageService> logger;
        public SendMessageService(ITelegramBotClient botClient, SendMessage Send, ILogger<SendMessageService> logger)
        {
            this.Send = Send;
            this.botClient = botClient;
            this.logger = logger;
        }


        public async Task SendDocument(InputFile inputFile, long ChatId, int replyTo)
        {
            await Send.AddTask(async () =>
            {
                var message = await botClient.SendDocument(
                    chatId: ChatId,
                    document: inputFile,
                    replyParameters: new ReplyParameters() { MessageId = replyTo }
                    );
            }, ChatId < 0);
        }
        public Task SendDocument(Stream inputFile, string FileName, long ChatId, int replyTo) => SendDocument(InputFile.FromStream(inputFile, FileName), ChatId, replyTo);
        public Task SendDocument(byte[] inputFile, string FileName, long ChatId, int replyTo) => SendDocument(InputFile.FromStream(new MemoryStream(inputFile), FileName), ChatId, replyTo);
        public Task SendDocument(string inputFile, string FileName, long ChatId, int replyTo) => SendDocument(InputFile.FromStream(new MemoryStream(Encoding.UTF8.GetBytes(inputFile)), FileName), ChatId, replyTo);


        public Task SendMessage(string Text, Chat ChatId, int replyTo) => SendMessage(Text, ChatId.Id, replyTo);
        public async Task SendMessage(string Text, long ChatId, int replyTo)
        {
            await Send.AddTask(async () =>
            {
                await botClient.SendMessage(
                    chatId: ChatId,
                    disableNotification: true,
                    replyParameters: new ReplyParameters() { MessageId = replyTo },
                    text: Text
                    );
            }, ChatId < 0);
        }
        public async Task SendMessage(string Text, long ChatId)
        {
            await Send.AddTask(async () =>
            {
                await botClient.SendMessage(
                    chatId: ChatId,
                    disableNotification: true,
                    text: Text
                    );
            }, ChatId < 0);
        }
        public async IAsyncEnumerable<Model.Data.Message> SendMessage(IAsyncEnumerable<string> messages, long ChatId, int replyTo, string InitialContent = "Initializing...")
        {
            // 初始化一条消息，准备编辑
            var sentMessage = await botClient.SendMessage(
                chatId: ChatId,
                text: InitialContent,
                replyParameters: new ReplyParameters() { MessageId = replyTo }
            );
            StringBuilder builder = new StringBuilder();
            var tmpMessageId = sentMessage.MessageId;
            var datetime = DateTime.UtcNow;
            await foreach (var PerMessage in messages)
            {
                if (builder.Length > 1900)
                {
                    tmpMessageId = sentMessage.MessageId;
                    yield return new Model.Data.Message()
                    {
                        GroupId = ChatId,
                        MessageId = sentMessage.MessageId,
                        DateTime = sentMessage.Date,
                        ReplyToUserId = (await botClient.GetMe()).Id,
                        ReplyToMessageId = tmpMessageId,
                        FromUserId = (await botClient.GetMe()).Id,
                        Content = builder.ToString(),
                    };
                    sentMessage = await botClient.SendMessage(
                        chatId: ChatId,
                        text: InitialContent,
                        replyParameters: new ReplyParameters() { MessageId = tmpMessageId }
                        );
                    builder.Clear();
                }
                builder.Append(PerMessage);
                if (DateTime.UtcNow - datetime > TimeSpan.FromSeconds(5))
                {
                    datetime = DateTime.UtcNow;
                    await Send.AddTask(async () =>
                    {
                        await botClient.EditMessageText(
                            chatId: sentMessage.Chat.Id,
                            messageId: sentMessage.MessageId,
                            parseMode: ParseMode.None,
                            text: builder.ToString()
                            );
                    }, ChatId < 0);
                }
            }
            await Send.AddTask(async () =>
            {
                var message = await botClient.EditMessageText(
                    chatId: sentMessage.Chat.Id,
                    messageId: sentMessage.MessageId,
                    parseMode: ParseMode.None,
                    text: builder.ToString()
                    );
                logger.LogInformation($"Send OpenAI result success {message.MessageId} {builder.ToString()}");
            }, ChatId < 0);
            yield return new Model.Data.Message()
            {
                GroupId = ChatId,
                MessageId = sentMessage.MessageId,
                DateTime = sentMessage.Date,
                ReplyToUserId = (await botClient.GetMe()).Id,
                FromUserId = (await botClient.GetMe()).Id,
                ReplyToMessageId = tmpMessageId,
                Content = builder.ToString(),
            };
        }
    }
}

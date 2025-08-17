using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Service.BotAPI
{
    public partial class SendMessageService
    {
        #region Standard Send Methods
        public async Task SendVideoAsync(InputFile video, string caption, long chatId, int replyTo, ParseMode parseMode = ParseMode.MarkdownV2)
        {
            await AddTask(async () =>
            {
                await botClient.SendVideo(
                    chatId: chatId,
                    video: video,
                    caption: caption,
                    parseMode: parseMode,
                    replyParameters: new ReplyParameters() { MessageId = replyTo }
                );
            }, chatId < 0);
        }

        public async Task SendMediaGroupAsync(IEnumerable<IAlbumInputMedia> mediaGroup, long chatId, int replyTo)
        {
            await AddTask(async () =>
            {
                await botClient.SendMediaGroup(
                    chatId: chatId,
                    media: mediaGroup,
                    replyParameters: new ReplyParameters() { MessageId = replyTo }
                );
            }, chatId < 0);
        }

        public async Task SendDocument(InputFile inputFile, long ChatId, int replyTo)
        {
            await AddTask(async () =>
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
            await AddTask(async () =>
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
            await AddTask(async () =>
            {
                await botClient.SendMessage(
                    chatId: ChatId,
                    disableNotification: true,
                    text: Text
                    );
            }, ChatId < 0);
        }
        #endregion
    }
}

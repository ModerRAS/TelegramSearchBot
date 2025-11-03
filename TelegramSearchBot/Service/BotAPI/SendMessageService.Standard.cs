using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Core.Interface;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Service.BotAPI {
    public partial class SendMessageService {
        #region Standard Send Methods
        public async Task SendVideoAsync(InputFile video, string caption, long chatId, int replyTo, ParseMode parseMode = ParseMode.MarkdownV2) {
            await Send.AddTask(async () => {
                await botClient.SendVideo(
                    chatId: chatId,
                    video: video,
                    caption: caption,
                    parseMode: parseMode,
                    replyParameters: new ReplyParameters() { MessageId = replyTo }
                );
            }, chatId < 0);
        }

        public async Task SendMediaGroupAsync(IEnumerable<IAlbumInputMedia> mediaGroup, long chatId, int replyTo) {
            await Send.AddTask(async () => {
                await botClient.SendMediaGroup(
                    chatId: chatId,
                    media: mediaGroup,
                    replyParameters: new ReplyParameters() { MessageId = replyTo }
                );
            }, chatId < 0);
        }

        public async Task SendDocument(InputFile inputFile, long ChatId, int replyTo) {
            await Send.AddTask(async () => {
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
        public async Task SendMessage(string Text, long ChatId, int replyTo) {
            await Send.AddTask(async () => {
                await botClient.SendMessage(
                    chatId: ChatId,
                    disableNotification: true,
                    replyParameters: new ReplyParameters() { MessageId = replyTo },
                    text: Text
                    );
            }, ChatId < 0);
        }
        public async Task SendMessage(string Text, long ChatId) {
            await Send.AddTask(async () => {
                await botClient.SendMessage(
                    chatId: ChatId,
                    disableNotification: true,
                    text: Text
                    );
            }, ChatId < 0);
        }
        public Task SplitAndSendTextMessage(string Text, Chat ChatId, int replyTo) => SplitAndSendTextMessage(Text, ChatId.Id, replyTo);
        public async Task SplitAndSendTextMessage(string Text, long ChatId, int replyTo) {
            const int maxLength = 4096; // Telegram message length limit
            if (Text.Length <= maxLength) {
                await SendMessage(Text, ChatId, replyTo);
                return;
            }

            // Split text into chunks preserving words and markdown formatting
            var chunks = new List<string>();
            var currentChunk = new StringBuilder();
            var lines = Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            foreach (var line in lines) {
                if (currentChunk.Length + line.Length + 1 > maxLength) {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                }
                currentChunk.AppendLine(line);
            }

            if (currentChunk.Length > 0) {
                chunks.Add(currentChunk.ToString());
            }

            // Send chunks with page numbers
            for (int i = 0; i < chunks.Count; i++) {
                var chunkText = chunks[i];
                if (chunks.Count > 1) {
                    chunkText = $"{chunkText}\n\n({i + 1}/{chunks.Count})";
                }
                await SendMessage(chunkText, ChatId, replyTo);
            }
        }
        #endregion
    }
}

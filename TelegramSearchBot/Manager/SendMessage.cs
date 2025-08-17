using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RateLimiter;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Common;

namespace TelegramSearchBot.Manager {
    public class SendMessage : BackgroundService, ISendMessageService {
        private ConcurrentQueue<Task> tasks;
        private readonly TimeLimiter GroupLimit;
        private readonly TimeLimiter GlobalLimit;
        private readonly ITelegramBotClient botClient;
        private readonly ILogger<SendMessage> logger;
        //private List<Task> tasks;
        public SendMessage(ITelegramBotClient botClient, ILogger<SendMessage> logger) {
            //queue = new ConcurrentQueue<SendModel>();
            GroupLimit = TimeLimiter.GetFromMaxCountByInterval(20, TimeSpan.FromMinutes(1));
            GlobalLimit = TimeLimiter.GetFromMaxCountByInterval(30, TimeSpan.FromSeconds(1));
            tasks = new ConcurrentQueue<Task>();
            this.botClient = botClient;
            this.logger = logger;
        }
        public async Task Log(string Text) {
            logger.LogInformation(Text);
            await AddTask(async () => {
                await botClient.SendMessage(
                    chatId: Env.AdminId,
                    disableNotification: true,
                    parseMode: ParseMode.None,
                    text: Text
                    );
            }, false);
        }
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public virtual async Task AddTask(Func<Task> Action, bool IsGroup) {
            if (IsGroup) {
                tasks.Enqueue(GroupLimit.Enqueue(async () => await GlobalLimit.Enqueue(Action)));
            } else {
                tasks.Enqueue(GlobalLimit.Enqueue(Action));
            }
            //queue.Append(new SendModel() { Action = func, IsGroup = IsGroup });
            //Console.WriteLine(queue.Count());
        }

        public Task AddTextMessageToSend(
            long chatId,
            string text,
            ParseMode? parseMode = null,
            Telegram.Bot.Types.ReplyParameters? replyParameters = null,
            bool disableNotification = false,
            bool highPriorityForGroup = false, // To determine if it's a group message for rate limiting
            CancellationToken cancellationToken = default) {
            Func<Task> action = async () => {
                await botClient.SendMessage(
                    chatId: chatId,
                    text: text,
                    parseMode: parseMode.HasValue ? parseMode.Value : default, // Use default if null, or omit if API handles null
                    replyParameters: replyParameters,
                    disableNotification: disableNotification,
                    cancellationToken: cancellationToken
                );
            };
            // The 'IsGroup' parameter in AddTask seems to control which rate limiter to use.
            // We'll use highPriorityForGroup to map to IsGroup.
            // If it's a group message (highPriorityForGroup = true), it uses GroupLimit then GlobalLimit.
            // If not (highPriorityForGroup = false), it uses only GlobalLimit.
            return AddTask(action, highPriorityForGroup);
        }

        public async Task Run(CancellationToken stoppingToken) {

            while (true) {
                if (tasks.IsEmpty) {
                    await Task.Delay(1000);
                } else {
                    while (!tasks.IsEmpty) {
                        try {
                            if (tasks.TryDequeue(out var result)) {
                                result.Wait();
                            }
                        } catch (Exception) {
                        }
                    }
                }
                if (stoppingToken.IsCancellationRequested) {
                    logger.LogInformation("SendMessage service is stopping.");
                    return;
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            await Run(stoppingToken);
        }

        public Task<T> AddTaskWithResult<T>(Func<Task<T>> Action, bool IsGroup) {
            if (IsGroup) {
                return GroupLimit.Enqueue(async () => await GlobalLimit.Enqueue(Action));
            } else {
                return GlobalLimit.Enqueue(Action);
            }
        }
        public Task<T> AddTaskWithResult<T>(Func<Task<T>> Action, long ChatId) {
            if (ChatId < 0) {
                return GroupLimit.Enqueue(async () => await GlobalLimit.Enqueue(Action));
            } else {
                return GlobalLimit.Enqueue(Action);
            }
        }

        #region ISendMessageService 实现

        public async Task<Message> SendTextMessageAsync(string text, long chatId, int replyToMessageId = 0, bool disableNotification = false)
        {
            return await AddTaskWithResult(async () =>
            {
                return await botClient.SendMessage(
                    chatId: chatId,
                    text: text,
                    replyParameters: replyToMessageId != 0 ? new ReplyParameters { MessageId = replyToMessageId } : null,
                    disableNotification: disableNotification
                );
            }, chatId < 0);
        }

        public async Task SplitAndSendTextMessage(string text, long chatId, int replyToMessageId = 0)
        {
            // 简化实现：直接发送完整消息
            await SendTextMessageAsync(text, chatId, replyToMessageId);
        }

        public async Task<Message> SendButtonMessageAsync(string text, long chatId, int replyToMessageId = 0, params (string text, string callbackData)[] buttons)
        {
            // 创建内联键盘
            var inlineKeyboard = buttons.Select(b => new[] { InlineKeyboardButton.WithCallbackData(b.text, b.callbackData) }).ToArray();
            var replyMarkup = new InlineKeyboardMarkup(inlineKeyboard);

            return await AddTaskWithResult(async () =>
            {
                return await botClient.SendMessage(
                    chatId: chatId,
                    text: text,
                    replyParameters: replyToMessageId != 0 ? new ReplyParameters { MessageId = replyToMessageId } : null,
                    replyMarkup: replyMarkup
                );
            }, chatId < 0);
        }

        public async Task<Message> SendPhotoAsync(long chatId, InputFile photo, string caption = null, int replyToMessageId = 0, bool disableNotification = false)
        {
            return await AddTaskWithResult(async () =>
            {
                return await botClient.SendPhoto(
                    chatId: chatId,
                    photo: photo,
                    caption: caption,
                    replyParameters: replyToMessageId != 0 ? new ReplyParameters { MessageId = replyToMessageId } : null,
                    disableNotification: disableNotification
                );
            }, chatId < 0);
        }

        public async Task<List<TelegramSearchBot.Model.Data.Message>> SendFullMessageStream(
            IAsyncEnumerable<string> fullMessagesStream,
            long chatId,
            int replyTo,
            string initialPlaceholderContent = "⏳",
            CancellationToken cancellationToken = default)
        {
            // 简化实现：直接发送第一条消息
            var sentMessage = await AddTaskWithResult(async () =>
            {
                return await botClient.SendMessage(
                    chatId: chatId,
                    text: initialPlaceholderContent,
                    replyParameters: replyTo != 0 ? new ReplyParameters { MessageId = replyTo } : null
                );
            }, chatId < 0);

            // 收集所有消息内容
            var allContent = new List<string>();
            await foreach (var content in fullMessagesStream.WithCancellation(cancellationToken))
            {
                allContent.Add(content);
            }

            // 发送完整内容
            var finalContent = string.Join("", allContent);
            var finalMessage = await AddTaskWithResult(async () =>
            {
                return await botClient.SendMessage(
                    chatId: chatId,
                    text: finalContent,
                    replyParameters: replyTo != 0 ? new ReplyParameters { MessageId = replyTo } : null
                );
            }, chatId < 0);

            // 返回数据库消息列表
            return new List<TelegramSearchBot.Model.Data.Message>
            {
                TelegramSearchBot.Model.Data.Message.FromTelegramMessage(sentMessage),
                TelegramSearchBot.Model.Data.Message.FromTelegramMessage(finalMessage)
            };
        }

        #endregion
    }
}

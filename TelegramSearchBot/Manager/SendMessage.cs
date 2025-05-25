using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RateLimiter;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace TelegramSearchBot.Manager
{
    public class SendMessage : BackgroundService
    {
        private ConcurrentQueue<Task> tasks;
        private readonly TimeLimiter GroupLimit;
        private readonly TimeLimiter GlobalLimit;
        private readonly ITelegramBotClient botClient;
        private readonly ILogger<SendMessage> logger;
        //private List<Task> tasks;
        public SendMessage(ITelegramBotClient botClient, ILogger<SendMessage> logger)
        {
            //queue = new ConcurrentQueue<SendModel>();
            GroupLimit = TimeLimiter.GetFromMaxCountByInterval(20, TimeSpan.FromMinutes(1));
            GlobalLimit = TimeLimiter.GetFromMaxCountByInterval(30, TimeSpan.FromSeconds(1));
            tasks = new ConcurrentQueue<Task>();
            this.botClient = botClient;
            this.logger = logger;
        }
        public async Task Log(string Text)
        {
            logger.LogInformation(Text);
            await AddTask(async () =>
            {
                await botClient.SendMessage(
                    chatId: Env.AdminId,
                    disableNotification: true,
                    parseMode: ParseMode.None,
                    text: Text
                    );
            }, false);
        }
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public virtual async Task AddTask(Func<Task> Action, bool IsGroup)
        {
            if (IsGroup)
            {
                tasks.Enqueue(GroupLimit.Enqueue(async () => await GlobalLimit.Enqueue(Action)));
            }
            else
            {
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
            CancellationToken cancellationToken = default)
        {
            Func<Task> action = async () =>
            {
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

        public async Task Run(CancellationToken stoppingToken)
        {

            while (true)
            {
                if (tasks.IsEmpty)
                {
                    await Task.Delay(1000);
                }
                else
                {
                    while (!tasks.IsEmpty)
                    {
                        try
                        {
                            if (tasks.TryDequeue(out var result))
                            {
                                result.Wait();
                            }
                        }
                        catch (Exception)
                        {
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
    }
}

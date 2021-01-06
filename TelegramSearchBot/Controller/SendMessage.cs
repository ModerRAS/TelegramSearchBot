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
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Controller {
    class SendMessage {
        private ConcurrentQueue<Task> tasks;
        private readonly TimeLimiter GroupLimit;
        private readonly TimeLimiter GlobalLimit;
        private readonly ITelegramBotClient botClient;
        //private List<Task> tasks;
        public SendMessage(ITelegramBotClient botClient) {
            //queue = new ConcurrentQueue<SendModel>();
            GroupLimit = TimeLimiter.GetFromMaxCountByInterval(20, TimeSpan.FromMinutes(1));
            GlobalLimit = TimeLimiter.GetFromMaxCountByInterval(30, TimeSpan.FromSeconds(1));
            tasks = new ConcurrentQueue<Task>();
            this.botClient = botClient;
        }
        public async Task Log(string Text) {
            await AddTask(async () => {
                await botClient.SendTextMessageAsync(
                    chatId: Env.AdminId,
                    disableNotification: true,
                    parseMode: ParseMode.Default,
                    text: Text
                    );
            }, false);
        }
        public async Task AddTask(Func<Task> Action, bool IsGroup) {
            if (IsGroup) {
                tasks.Enqueue(GroupLimit.Enqueue(async () => await GlobalLimit.Enqueue(Action)));
            } else {
                tasks.Enqueue(GlobalLimit.Enqueue(Action));
            }
            //queue.Append(new SendModel() { Action = func, IsGroup = IsGroup });
            //Console.WriteLine(queue.Count());
        }
        public Task Run() {
            
            while (true) {
                if (tasks.IsEmpty) {
                    Thread.Sleep(1000);
                } else {
                    while (!tasks.IsEmpty) {
                        try {
                            Task result;
                            tasks.TryDequeue(out result);
                            result.Wait();
                        } catch (Exception) {
                            
                        }
                    }
                }
            }
        }
    }
}

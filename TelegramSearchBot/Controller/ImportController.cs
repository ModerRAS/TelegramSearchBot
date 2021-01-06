using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Controller {
    class ImportController : IOnMessage {
        private readonly SearchContext context;
        private readonly IDistributedCache Cache;
        private readonly ITelegramBotClient botClient;
        public ImportController(ITelegramBotClient botClient, SearchContext context, IDistributedCache Cache) {
            this.context = context;
            this.Cache = Cache;
            this.botClient = botClient;
        }

        public static async Task<string> CrawlString(string url, HttpClient client) {
            var tick = 5;
            while (tick > 0) {
                try {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");
                    var response = await client.SendAsync(request).ConfigureAwait(true);
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                } catch (HttpRequestException) {
                    tick--;
                }
            }
            throw new HttpRequestException();
        }

        public async Task ExecuteAsync(object sender, MessageEventArgs e) {
            if (e.Message.Chat.Id < 0) {
                return;
            }
            if (e.Message.Chat.Id != Env.AdminId) {
                return;
            }
            string Command;
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                Command = e.Message.Text;
            } else if (!string.IsNullOrEmpty(e.Message.Caption)) {
                Command = e.Message.Caption;
            } else return;
            if (Command.Length > 3 && Command.Substring(0,3).Equals("导入 ")) {
                var url = Command.Substring(3).Trim();
                var client = new HttpClient();
                var model = JsonConvert.DeserializeObject<CommonModel.ImportModel>(await CrawlString(url, client));
                //var keys = model.Messages.Keys.ToArray();
                //var messages = context.Messages.ToList();
                var missingRecords = model.Messages;//.Where(x => !messages.Any(z => z.MessageId == x.Key && z.ChatId == model.ChatId));
                var ToAdd = new List<Message>();
                foreach (var i in missingRecords) {
                    ToAdd.Add(new Message() {
                        GroupId = model.GroupId,
                        MessageId = i.Key,
                        Content = i.Value
                    }); ;
                }
                await context.Messages.AddRangeAsync(ToAdd);
                await context.SaveChangesAsync();
            }
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OllamaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service {
    public class OllamaService : IService {
        public string ServiceName => "OllamaService";

        private readonly ILogger _logger;
        public OllamaApiClient ollama { get; set; }
        public string BotName { get; set; }
        private DataDbContext _dbContext;

        public OllamaService(DataDbContext context, ILogger<OllamaService> logger) {
            _logger = logger;
            _dbContext = context;
            // set up the client
            var uri = new Uri(Env.OllamaHost);
            ollama = new OllamaApiClient(uri);

            // select a model which should be used for further operations
            ollama.SelectedModel = Env.OllamaModelName;

        }

        public bool CheckIfExists(IEnumerable<OllamaSharp.Models.Model> models) {
            foreach (var model in models) {
                if (model.Name.Equals(Env.OllamaModelName)) {
                    return true;
                }
            }
            return false;
        }
        

        public async IAsyncEnumerable<string> ExecAsync(string InputToken, long ChatId) {
            var models = await ollama.ListLocalModelsAsync();
            if (!CheckIfExists(models)) {
                await foreach (var status in ollama.PullModelAsync(Env.OllamaModelName))
                    _logger.LogInformation($"{status.Percent}% {status.Status}");
            }
            var Messages = (from s in _dbContext.Messages
                           where s.GroupId == ChatId && s.DateTime > DateTime.Now.AddHours(-1)
                           select s).ToList();
            _logger.LogInformation($"Ollama获取数据库得到{ChatId}中的{Messages.Count}条结果。");
            var MessagesJson = JsonConvert.SerializeObject(Messages, Formatting.Indented);
            var prompt = $"忘记你原有的名字，记住，你的名字叫：{BotName}，是一个问答机器人，在向你提问之前，我将给你提供以下聊天记录，以供参考,格式为Json格式的列表，其中每一条的聊天记录在Content字段内\n{MessagesJson}";
            var chat = new Chat(ollama, prompt);


            await foreach (var answerToken in chat.SendAsync(InputToken)) {
                yield return answerToken;
            }

        }
    }
}

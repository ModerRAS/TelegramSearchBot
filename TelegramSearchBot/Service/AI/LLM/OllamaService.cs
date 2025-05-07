using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OllamaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    public class OllamaService : IService, ILLMService
    {
        public string ServiceName => "OllamaService";

        private readonly ILogger _logger;
        public OllamaApiClient ollama { get; set; }
        public string BotName { get; set; }
        private DataDbContext _dbContext;

        public OllamaService(DataDbContext context, ILogger<OllamaService> logger)
        {
            _logger = logger;
            _dbContext = context;
            
        }

        public bool CheckIfExists(IEnumerable<OllamaSharp.Models.Model> models, string ModelName)
        {
            foreach (var model in models)
            {
                if (model.Name.Equals(ModelName))
                {
                    return true;
                }
            }
            return false;
        }


        public async IAsyncEnumerable<string> ExecAsync(Model.Data.Message message, long ChatId, string modelName, LLMChannel channel) {
            // set up the client
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(channel.Gateway);
            client.Timeout = TimeSpan.FromSeconds(3000);
            ollama = new OllamaApiClient(client);

            // select a model which should be used for further operations
            ollama.SelectedModel = modelName ?? Env.OllamaModelName;
            var models = await ollama.ListLocalModelsAsync();
            if (!CheckIfExists(models, modelName))
            {
                await foreach (var status in ollama.PullModelAsync(modelName))
                    _logger.LogInformation($"{status.Percent}% {status.Status}");
            }
            var Messages = (from s in _dbContext.Messages
                            where s.GroupId == ChatId && s.DateTime > DateTime.UtcNow.AddHours(-1)
                            select s).ToList();
            if (Messages.Count < 10)
            {
                Messages = (from s in _dbContext.Messages
                            where s.GroupId == ChatId
                            orderby s.DateTime descending
                            select s).Take(10).ToList();

                Messages.Reverse(); // ✅ 记得倒序回来，按时间正序处理
            }

            _logger.LogInformation($"Ollama获取数据库得到{ChatId}中的{Messages.Count}条结果。");
            var MessagesJson = JsonConvert.SerializeObject(Messages, Formatting.Indented);
            var prompt = $"忘记你原有的名字，记住，你的名字叫：{BotName}，是一个问答机器人，在向你提问之前，我将给你提供以下聊天记录，以供参考,格式为Json格式的列表，其中每一条的聊天记录在Content字段内\n{MessagesJson}";
            var chat = new Chat(ollama, prompt);


            await foreach (var answerToken in chat.SendAsync(message.Content))
            {
                yield return answerToken;
            }

        }
    }
}

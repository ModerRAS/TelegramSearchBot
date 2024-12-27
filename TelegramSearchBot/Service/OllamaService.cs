using Microsoft.Extensions.Logging;
using OllamaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;

namespace TelegramSearchBot.Service {
    public class OllamaService : IService {
        public string ServiceName => "OllamaService";

        private readonly ILogger _logger;
        public OllamaApiClient ollama { get; set; }
        public string BotName { get; set; }
        public Dictionary<long, Chat> ChatWithId { get; set; }

        public OllamaService(ILogger<OllamaService> logger) {
            _logger = logger;
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
            if (!ChatWithId.ContainsKey(ChatId)) {
                ChatWithId.Add(ChatId, new Chat(ollama, $"忘记你原有的名字，记住，你的名字叫：${BotName}，是一个问答机器人"));
            }
            
            await foreach (var answerToken in ChatWithId[ChatId].SendAsync(InputToken)) {
                yield return answerToken;
            }

        }
    }
}

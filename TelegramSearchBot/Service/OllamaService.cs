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

        public async IAsyncEnumerable<string> ExecAsync(string InputToken) {
            var models = await ollama.ListLocalModelsAsync();
            if (!CheckIfExists(models)) {
                await foreach (var status in ollama.PullModelAsync(Env.OllamaModelName))
                    _logger.LogInformation($"{status.Percent}% {status.Status}");
            }
            var chat = new Chat(ollama);
            await foreach (var answerToken in chat.SendAsync(InputToken)) {
                yield return answerToken;
            }

        }
    }
}

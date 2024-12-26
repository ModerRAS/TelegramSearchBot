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

        private readonly string ModelName = "qwen2.5:72b-instruct-q2_K";
        private readonly string Host = "http://localhost:11434";
        private readonly ILogger _logger;
        public OllamaApiClient ollama { get; set; }

        public OllamaService(ILogger<OllamaService> logger) {
            _logger = logger;
            // set up the client
            var uri = new Uri(Host);
            ollama = new OllamaApiClient(uri);

            // select a model which should be used for further operations
            ollama.SelectedModel = ModelName;

        }

        public bool CheckIfExists(IEnumerable<OllamaSharp.Models.Model> models) {
            foreach (var model in models) {
                if (model.Name.Equals(ModelName)) {
                    return true;
                }
            }
            return false;
        }

        public async Task ExecAsync(string InputToken) {
            var models = await ollama.ListLocalModelsAsync();
            if (!CheckIfExists(models)) {
                await foreach (var status in ollama.PullModelAsync(ModelName))
                    _logger.LogInformation($"{status.Percent}% {status.Status}");
            }
            
        }
    }
}

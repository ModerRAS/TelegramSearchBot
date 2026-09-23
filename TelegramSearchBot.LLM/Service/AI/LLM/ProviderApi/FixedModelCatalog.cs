using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// Catalog for providers that do not have a shared model-list HTTP API here.
    /// Returns the preset's seeded model ids and never calls the network, so refresh cannot wipe manual rows.
    /// </summary>
    [Injectable(ServiceLifetime.Transient)]
    public class FixedModelCatalog : ILlmModelCatalog, ILlmEmbeddings, ILlmVision {
        public Task<bool> IsHealthyAsync(LLMChannel channel, LLMApiBinding binding) =>
            Task.FromResult(channel != null && !string.IsNullOrWhiteSpace(channel.ApiKey));

        public Task<IEnumerable<string>> GetAllModels(LLMChannel channel) {
            if (channel == null) return Task.FromResult<IEnumerable<string>>([]);
            var names = LlmProviderCatalog.Presets
                .Where(preset => preset.Provider == channel.Provider)
                .SelectMany(preset => preset.DefaultModels)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult<IEnumerable<string>>(names);
        }

        public async Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel) {
            var models = await GetAllModels(channel);
            return models.Select(name => new ModelWithCapabilities { ModelName = name });
        }

        public Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel) =>
            throw new NotSupportedException($"{channel?.Provider} does not provide embeddings.");

        public Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, string prompt = null) =>
            throw new NotSupportedException($"{channel?.Provider} does not provide vision analysis.");
    }
}

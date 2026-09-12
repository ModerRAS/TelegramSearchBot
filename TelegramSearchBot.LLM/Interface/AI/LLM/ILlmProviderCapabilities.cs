using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Interface.AI.LLM {
    /// <summary>Per-dialect model discovery (list + capability merge). Implemented by OpenAI/MiniMax/LMStudio, Anthropic, Responses, Gemini, Ollama APIs.</summary>
    public interface ILlmModelCatalog {
        Task<IEnumerable<string>> GetAllModels(LLMChannel channel);
        Task<IEnumerable<ModelWithCapabilities>> GetAllModelsWithCapabilities(LLMChannel channel);
    }

    /// <summary>Embedding generation (OpenAI/Responses/Gemini/Ollama; not Anthropic).</summary>
    public interface ILlmEmbeddings {
        Task<float[]> GenerateEmbeddingsAsync(string text, string modelName, LLMChannel channel);
    }

    /// <summary>Vision image analysis (OpenAI-style chat vision; not Anthropic/Ollama).</summary>
    public interface ILlmVision {
        Task<string> AnalyzeImageAsync(string photoPath, string modelName, LLMChannel channel, string prompt = null);
    }
}

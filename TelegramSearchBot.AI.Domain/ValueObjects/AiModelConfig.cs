using System;

namespace TelegramSearchBot.AI.Domain.ValueObjects
{
    /// <summary>
    /// AI模型配置值对象
    /// </summary>
    public class AiModelConfig : IEquatable<AiModelConfig>
    {
        public string ModelName { get; }
        public string ModelType { get; }
        public string? Endpoint { get; }
        public string? ApiKey { get; }
        public int MaxTokens { get; }
        public double Temperature { get; }
        public Dictionary<string, object> AdditionalParameters { get; }

        public AiModelConfig(string modelName, string modelType, string? endpoint = null, 
            string? apiKey = null, int maxTokens = 4096, double temperature = 0.7, 
            Dictionary<string, object>? additionalParameters = null)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                throw new ArgumentException("Model name cannot be null or empty", nameof(modelName));
            
            if (string.IsNullOrWhiteSpace(modelType))
                throw new ArgumentException("Model type cannot be null or empty", nameof(modelType));

            ModelName = modelName;
            ModelType = modelType;
            Endpoint = endpoint;
            ApiKey = apiKey;
            MaxTokens = maxTokens > 0 ? maxTokens : throw new ArgumentException("Max tokens must be positive", nameof(maxTokens));
            Temperature = temperature is >= 0 and <= 2 ? temperature : throw new ArgumentException("Temperature must be between 0 and 2", nameof(temperature));
            AdditionalParameters = additionalParameters ?? new Dictionary<string, object>();
        }

        public static AiModelConfig CreateOllamaConfig(string modelName, string? endpoint = null, 
            int maxTokens = 4096, double temperature = 0.7, Dictionary<string, object>? additionalParameters = null)
        {
            return new AiModelConfig(modelName, "Ollama", endpoint, null, maxTokens, temperature, additionalParameters);
        }

        public static AiModelConfig CreateOpenAIConfig(string modelName, string apiKey, 
            string? endpoint = null, int maxTokens = 4096, double temperature = 0.7, 
            Dictionary<string, object>? additionalParameters = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be null or empty for OpenAI", nameof(apiKey));

            return new AiModelConfig(modelName, "OpenAI", endpoint, apiKey, maxTokens, temperature, additionalParameters);
        }

        public static AiModelConfig CreateGeminiConfig(string modelName, string apiKey, 
            string? endpoint = null, int maxTokens = 4096, double temperature = 0.7, 
            Dictionary<string, object>? additionalParameters = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be null or empty for Gemini", nameof(apiKey));

            return new AiModelConfig(modelName, "Gemini", endpoint, apiKey, maxTokens, temperature, additionalParameters);
        }

        public bool IsOllama => ModelType.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
        public bool IsOpenAI => ModelType.Equals("OpenAI", StringComparison.OrdinalIgnoreCase);
        public bool IsGemini => ModelType.Equals("Gemini", StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj)
        {
            return Equals(obj as AiModelConfig);
        }

        public bool Equals(AiModelConfig other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return ModelName == other.ModelName &&
                   ModelType == other.ModelType &&
                   Endpoint == other.Endpoint &&
                   ApiKey == other.ApiKey &&
                   MaxTokens == other.MaxTokens &&
                   Temperature == other.Temperature &&
                   AdditionalParameters.Equals(other.AdditionalParameters);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ModelName, ModelType, Endpoint, ApiKey, MaxTokens, Temperature, AdditionalParameters);
        }

        public static bool operator ==(AiModelConfig left, AiModelConfig right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(AiModelConfig left, AiModelConfig right)
        {
            return !(left == right);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using OllamaSharp;
using OllamaSharp.Models;

namespace TelegramSearchBot.Service.Vector
{
    public class VectorGenerationService : IService
    {
        private readonly QdrantClient _qdrantClient;
        private readonly OpenAIService _openAIService;
        private readonly OllamaService _ollamaService;

        public string ServiceName => "VectorGenerationService";

        public VectorGenerationService(
            QdrantClient qdrantClient,
            OpenAIService openAIService,
            OllamaService ollamaService)
        {
            _qdrantClient = qdrantClient;
            _openAIService = openAIService;
            _ollamaService = ollamaService;
        }

        public async Task<float[]> GenerateVectorAsync(string text, string modelName = "ollama", LLMChannel channel = null)
        {
            return modelName switch
            {
                //"openai" => await _openAIService.GenerateEmbeddingsAsync(text, modelName, channel),
                "ollama" => await _ollamaService.GenerateEmbeddingsAsync(text, modelName, channel),
                _ => throw new ArgumentException("Invalid model specified")
            };
        }

        public async Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> Payload)
        {
            var points = new[] { new PointStruct { Id = id, Vectors = vector } };
            foreach (var e in Payload) {
                points[0].Payload.Add(e.Key, e.Value);
            }
            await _qdrantClient.UpsertAsync(collectionName, points);
        }
        public async Task StoreVectorAsync(string collectionName, float[] vector, long MessageId) {
            var points = new[] { new PointStruct { Id = Guid.NewGuid(), Vectors = vector } };
            points[0].Payload.Add("MessageId", MessageId);
            await _qdrantClient.UpsertAsync(collectionName, points);
        }

        public async Task<float[][]> GenerateVectorsAsync(IEnumerable<string> texts, string modelName = "openai", LLMChannel channel = null)
        {
            var tasks = texts.Select(text => GenerateVectorAsync(text, modelName, channel));
            return await Task.WhenAll(tasks);
        }

        public async Task<IEnumerable<string>> SearchSimilarAsync(
            string collectionName,
            float[] queryVector,
            int limit = 5)
        {
            var result = await _qdrantClient.SearchAsync(
                collectionName,
                queryVector,
                limit: (uint)limit);

            return result.Select(x => x.Id.Num.ToString());
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                await _qdrantClient.ListCollectionsAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
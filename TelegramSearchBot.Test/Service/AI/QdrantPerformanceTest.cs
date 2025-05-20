using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.Vector;

namespace TelegramSearchBot.Test.Service.AI
{
    [TestClass]
    public class QdrantPerformanceTest
    {
        private VectorGenerationService _vectorService;
        private QdrantManager _qdrantManager;
        private OllamaService _ollamaService;

        [TestInitialize]
        public void Initialize()
        {
            _ollamaService = new OllamaService(/* test config */);
            _qdrantManager = new QdrantManager(/* test config */);
            _vectorService = new VectorGenerationService(
                _qdrantManager.GetClient(),
                null, // OpenAIService not used in tests
                _ollamaService);
        }

        [TestMethod]
        [DataRow(10)]    // Short text
        [DataRow(100)]   // Medium text
        [DataRow(1000)]  // Long text
        public async Task TestVectorGenerationPerformance(int textLength)
        {
            var testText = GenerateRandomText(textLength);
            var stopwatch = Stopwatch.StartNew();
            
            var vector = await _vectorService.GenerateVectorAsync(
                testText,
                "ollama",
                new LLMChannel { Model = "llama2" });
            
            stopwatch.Stop();
            Debug.WriteLine($"Text length: {textLength} chars, " +
                $"Vector generation time: {stopwatch.ElapsedMilliseconds}ms");
        }

        [TestMethod]
        [DataRow(1000)]   // Small collection
        [DataRow(10000)]  // Medium collection
        [DataRow(100000)] // Large collection
        public async Task TestQdrantSearchPerformance(int collectionSize)
        {
            var vectors = GenerateTestVectors(collectionSize);
            await _qdrantManager.StoreVectorsAsync(vectors);
            
            var queryVector = GenerateRandomVector();
            var stopwatch = Stopwatch.StartNew();
            
            var results = await _vectorService.SearchSimilarAsync(
                "test_collection",
                queryVector);
            
            stopwatch.Stop();
            Debug.WriteLine($"Collection size: {collectionSize}, " +
                $"Search time: {stopwatch.ElapsedMilliseconds}ms");
        }

        private string GenerateRandomText(int length)
        {
            // Implementation for generating random text
            var random = new Random();
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private List<float[]> GenerateTestVectors(int count)
        {
            // Implementation for generating test vectors
            var random = new Random();
            var vectors = new List<float[]>();
            for (int i = 0; i < count; i++)
            {
                var vector = new float[1536]; // Typical embedding size
                for (int j = 0; j < vector.Length; j++)
                {
                    vector[j] = (float)random.NextDouble();
                }
                vectors.Add(vector);
            }
            return vectors;
        }

        private float[] GenerateRandomVector()
        {
            // Implementation for generating random query vector
            var random = new Random();
            var vector = new float[1536]; // Match embedding size
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)random.NextDouble();
            }
            return vector;
        }
    }
}
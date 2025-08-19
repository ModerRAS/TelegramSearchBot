using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Interface;
using Xunit.Abstractions;
using FluentAssertions;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Search.Tests.Helpers
{
    /// <summary>
    /// 搜索测试辅助类
    /// 提供简化的测试辅助方法
    /// </summary>
    public static class SearchTestHelpers
    {
        /// <summary>
        /// 创建简化的测试消息
        /// </summary>
        public static Message CreateTestMessage(long groupId, long messageId, long fromUserId, string content)
        {
            return new Message
            {
                GroupId = groupId,
                MessageId = messageId,
                FromUserId = fromUserId,
                Content = content,
                DateTime = DateTime.UtcNow,
                MessageExtensions = new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 创建批量测试消息
        /// </summary>
        public static List<Message> CreateBulkTestMessages(int count, long groupId = 100)
        {
            var messages = new List<Message>();
            var baseTime = DateTime.UtcNow.AddHours(-24);
            var random = new Random();
            
            for (int i = 0; i < count; i++)
            {
                messages.Add(new Message
                {
                    GroupId = groupId,
                    MessageId = groupId * 10000 + i,
                    FromUserId = (i % 10) + 1,
                    Content = $"Test message {i} with content about search functionality. " +
                             $"This message contains keywords like 'search', 'test', 'lucene', 'vector', 'faiss'. " +
                             $"Random number: {random.Next(1, 1000)}",
                    DateTime = baseTime.AddMinutes(i),
                    MessageExtensions = new List<MessageExtension>()
                });
            }
            
            return messages;
        }

        /// <summary>
        /// 创建测试向量
        /// </summary>
        public static float[] CreateTestVector(int dimension)
        {
            var random = new Random();
            var vector = new float[dimension];
            
            for (int i = 0; i < dimension; i++)
            {
                vector[i] = (float)random.NextDouble();
            }
            
            // Normalize vector
            var magnitude = Math.Sqrt(vector.Sum(x => x * x));
            for (int i = 0; i < dimension; i++)
            {
                vector[i] /= (float)magnitude;
            }
            
            return vector;
        }

        /// <summary>
        /// 创建相似向量
        /// </summary>
        public static float[] CreateSimilarVector(float[] baseVector, float similarity)
        {
            var dimension = baseVector.Length;
            var random = new Random();
            var similarVector = new float[dimension];
            
            for (int i = 0; i < dimension; i++)
            {
                similarVector[i] = baseVector[i] + (float)(random.NextDouble() - 0.5) * similarity;
            }
            
            // Normalize vector
            var magnitude = Math.Sqrt(similarVector.Sum(x => x * x));
            for (int i = 0; i < dimension; i++)
            {
                similarVector[i] /= (float)magnitude;
            }
            
            return similarVector;
        }

        /// <summary>
        /// 验证搜索结果
        /// </summary>
        public static void ValidateSearchResults(List<Message> results, int expectedCount, string expectedKeyword)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));
                
            results.Should().HaveCount(expectedCount);
            
            foreach (var message in results)
            {
                message.Content.ToLower().Should().Contain(expectedKeyword.ToLower());
            }
        }

        /// <summary>
        /// 计算余弦相似度
        /// </summary>
        public static float CalculateCosineSimilarity(float[] vector1, float[] vector2)
        {
            if (vector1.Length != vector2.Length)
                return 0f;

            var dotProduct = 0f;
            var magnitude1 = 0f;
            var magnitude2 = 0f;

            for (int i = 0; i < vector1.Length; i++)
            {
                dotProduct += vector1[i] * vector2[i];
                magnitude1 += vector1[i] * vector1[i];
                magnitude2 += vector2[i] * vector2[i];
            }

            magnitude1 = (float)Math.Sqrt(magnitude1);
            magnitude2 = (float)Math.Sqrt(magnitude2);

            if (magnitude1 == 0 || magnitude2 == 0)
                return 0f;

            return dotProduct / (magnitude1 * magnitude2);
        }

        /// <summary>
        /// 创建测试搜索选项
        /// </summary>
        public static TelegramSearchBot.Model.SearchOption CreateSearchOption(string searchTerm, long? chatId = null, bool isGroup = true, 
            int skip = 0, int take = 10)
        {
            return new TelegramSearchBot.Model.SearchOption
            {
                Search = searchTerm,
                ChatId = chatId ?? 100,
                IsGroup = isGroup,
                Skip = skip,
                Take = take
            };
        }

        /// <summary>
        /// 等待条件满足
        /// </summary>
        public static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 5000, int checkIntervalMs = 100)
        {
            var startTime = DateTime.UtcNow;
            
            while (!condition())
            {
                if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException("Condition not met within timeout");
                    
                await Task.Delay(checkIntervalMs);
            }
        }

        /// <summary>
        /// 测量操作执行时间
        /// </summary>
        public static async Task<TimeSpan> MeasureExecutionTimeAsync(Func<Task> operation)
        {
            var startTime = DateTime.UtcNow;
            await operation();
            return DateTime.UtcNow - startTime;
        }

        /// <summary>
        /// 测量操作执行时间
        /// </summary>
        public static TimeSpan MeasureExecutionTime(Action operation)
        {
            var startTime = DateTime.UtcNow;
            operation();
            return DateTime.UtcNow - startTime;
        }

        /// <summary>
        /// 创建临时目录
        /// </summary>
        public static string CreateTempDirectory()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"SearchTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempPath);
            return tempPath;
        }

        /// <summary>
        /// 清理临时目录
        /// </summary>
        public static void CleanupTempDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception)
            {
                // 忽略清理错误
            }
        }

        /// <summary>
        /// 生成随机字符串
        /// </summary>
        public static string GenerateRandomString(int length = 10)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        /// <summary>
        /// 生成随机中文文本
        /// </summary>
        public static string GenerateRandomChineseText(int length = 10)
        {
            // 常用中文字符
            const string chineseChars = "的一是在不了有和人这中大为上个国我以要他时来用们生到作地于出就分对成会可主发年动同工也能下过子说产种面而方后多定行学法所民得经十三之进着等部度家电力里如水化高自二理起小物现实加量都两体制机当使点从业本去把性好应开它合还因由其些然前外天政四日那社义事平形相全表间样与关各重新线内数正心反你明看原又么利比或但质气第向道命此变条只没结解问意建月公无系军很情者最立代想已通并提直题党程展五果料象员革位入常文总次品式活设及管特件长求老头基资边流路级少图山统接知较将组见计别她手角期根论运农指几九区强放决西被干做必战先回则任取据处队南给色光门即保治北造百规热领七海口东导器压志世金增争济阶油思术极交受联什认六共权收证改清己美再采转更单风切打白教速花带安场身车例真务具万每目至达走积示议声报斗完类八离华名确才科张信马节话米整空元况今集温传土许步群广石记需段研界拉林律叫且究观越织装影算低持音众书布复容儿须际商非验连断深难近矿千周委素技备半办青省列习响约支般史感劳便团往酸历市克何除消构府称太准精值号率族维划选标写存候毛亲快效斯院查江型眼王按格养易置派层片始却专状育厂京识适属圆包火住调满县局照参红细引听该铁价严龙飞";
            var random = new Random();
            return new string(Enumerable.Repeat(chineseChars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}
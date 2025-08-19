using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Common.Interface.Bilibili;

namespace TelegramSearchBot.Test.Infrastructure
{
    /// <summary>
    /// 测试用的向量生成服务接口
    /// </summary>
    public interface IVectorGenerationService
    {
        Task<TelegramSearchBot.Model.SearchOption> Search(TelegramSearchBot.Model.SearchOption searchOption);
        Task<float[]> GenerateVectorAsync(string text);
        Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> payload);
        Task StoreVectorAsync(string collectionName, float[] vector, long messageId);
        Task StoreMessageAsync(Message message);
        Task<float[][]> GenerateVectorsAsync(IEnumerable<string> texts);
        Task<bool> IsHealthyAsync();
        Task VectorizeGroupSegments(long groupId);
        Task VectorizeConversationSegment(ConversationSegment segment);
    }

    /// <summary>
    /// 测试用的向量搜索服务接口
    /// </summary>
    public interface IVectorSearchService
    {
        Task<List<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 10, CancellationToken cancellationToken = default);
        Task<bool> IndexDocumentAsync(string id, float[] vector, Dictionary<string, string> metadata, CancellationToken cancellationToken = default);
        Task<bool> DeleteDocumentAsync(string id, CancellationToken cancellationToken = default);
        Task<bool> ClearIndexAsync(CancellationToken cancellationToken = default);
        bool IsAvailable();
        int GetIndexSize();
    }

    /// <summary>
    /// 测试用的B站服务接口
    /// </summary>
    public interface IBilibiliService
    {
        Task<BilibiliVideoInfo> GetVideoInfoAsync(string bvid, CancellationToken cancellationToken = default);
        Task<string> ExtractVideoUrlAsync(string url, CancellationToken cancellationToken = default);
        Task<bool> ValidateUrlAsync(string url, CancellationToken cancellationToken = default);
        bool IsAvailable();
    }

    /// <summary>
    /// 向量搜索结果
    /// </summary>
    public class VectorSearchResult
    {
        public string Id { get; set; } = string.Empty;
        public float Score { get; set; }
        public string Content { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// B站视频信息
    /// </summary>
    public class BilibiliVideoInfo
    {
        public string Bvid { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public int PlayCount { get; set; }
        public int LikeCount { get; set; }
        public int Duration { get; set; }
        public DateTime PublishDate { get; set; }
    }

    /// <summary>
    /// 测试数据集
    /// </summary>
    public class TestDataSet
    {
        public List<Message> Messages { get; set; } = new();
        public List<UserData> Users { get; set; } = new();
        public List<GroupData> Groups { get; set; } = new();
        public List<UserWithGroup> UsersWithGroups { get; set; } = new();
    }

    /// <summary>
    /// 数据库快照
    /// </summary>
    public class DatabaseSnapshot
    {
        public List<Message> Messages { get; set; } = new();
        public List<UserData> Users { get; set; } = new();
        public List<GroupData> Groups { get; set; } = new();
        public List<UserWithGroup> UsersWithGroups { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 对话段落
    /// </summary>
    public class ConversationSegment
    {
        public long GroupId { get; set; }
        public long StartMessageId { get; set; }
        public long EndMessageId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }
}
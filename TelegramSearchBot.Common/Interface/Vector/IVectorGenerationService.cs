using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using SearchOption = TelegramSearchBot.Model.SearchOption;

namespace TelegramSearchBot.Interface.Vector
{
    /// <summary>
    /// 向量生成服务接口
    /// 定义向量生成、存储和搜索的核心功能
    /// </summary>
    public interface IVectorGenerationService
    {
        /// <summary>
        /// 向量搜索
        /// </summary>
        /// <param name="searchOption">搜索选项</param>
        /// <returns>搜索结果</returns>
        Task<SearchOption> Search(SearchOption searchOption);
        
        /// <summary>
        /// 生成向量
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <returns>向量数组</returns>
        Task<float[]> GenerateVectorAsync(string text);
        
        /// <summary>
        /// 存储向量（兼容旧版本）
        /// </summary>
        /// <param name="collectionName">集合名称</param>
        /// <param name="id">ID</param>
        /// <param name="vector">向量</param>
        /// <param name="payload">负载数据</param>
        /// <returns>任务</returns>
        Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> payload);
        
        /// <summary>
        /// 存储向量（使用消息ID）
        /// </summary>
        /// <param name="collectionName">集合名称</param>
        /// <param name="vector">向量</param>
        /// <param name="messageId">消息ID</param>
        /// <returns>任务</returns>
        Task StoreVectorAsync(string collectionName, float[] vector, long messageId);
        
        /// <summary>
        /// 存储消息向量
        /// </summary>
        /// <param name="message">消息实体</param>
        /// <returns>任务</returns>
        Task StoreMessageAsync(Message message);
        
        /// <summary>
        /// 批量生成向量
        /// </summary>
        /// <param name="texts">文本集合</param>
        /// <returns>向量数组集合</returns>
        Task<float[][]> GenerateVectorsAsync(IEnumerable<string> texts);
        
        /// <summary>
        /// 健康检查
        /// </summary>
        /// <returns>是否健康</returns>
        Task<bool> IsHealthyAsync();
        
        /// <summary>
        /// 批量向量化群组的所有对话段
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <returns>任务</returns>
        Task VectorizeGroupSegments(long groupId);
        
        /// <summary>
        /// 向量化对话段
        /// </summary>
        /// <param name="segment">对话段</param>
        /// <returns>任务</returns>
        Task VectorizeConversationSegment(ConversationSegment segment);
    }
}
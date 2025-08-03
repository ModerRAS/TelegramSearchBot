using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Interface
{
    /// <summary>
    /// Lucene索引管理器接口
    /// 定义Lucene搜索引擎的核心操作
    /// </summary>
    public interface ILuceneManager
    {
        /// <summary>
        /// 写入文档到索引
        /// </summary>
        /// <param name="message">消息数据</param>
        /// <returns>异步任务</returns>
        Task WriteDocumentAsync(Message message);

        /// <summary>
        /// 搜索指定群组的消息
        /// </summary>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="groupId">群组ID</param>
        /// <param name="skip">跳过数量</param>
        /// <param name="take">获取数量</param>
        /// <returns>匹配数量和消息列表</returns>
        Task<(int, List<Message>)> Search(string keyword, long groupId, int skip = 0, int take = 20);

        /// <summary>
        /// 搜索所有群组的消息
        /// </summary>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="skip">跳过数量</param>
        /// <param name="take">获取数量</param>
        /// <returns>匹配数量和消息列表</returns>
        Task<(int, List<Message>)> SearchAll(string keyword, int skip = 0, int take = 20);

        /// <summary>
        /// 语法搜索指定群组的消息
        /// </summary>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="groupId">群组ID</param>
        /// <param name="skip">跳过数量</param>
        /// <param name="take">获取数量</param>
        /// <returns>匹配数量和消息列表</returns>
        Task<(int, List<Message>)> SyntaxSearch(string keyword, long groupId, int skip = 0, int take = 20);

        /// <summary>
        /// 语法搜索所有群组的消息
        /// </summary>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="skip">跳过数量</param>
        /// <param name="take">获取数量</param>
        /// <returns>匹配数量和消息列表</returns>
        Task<(int, List<Message>)> SyntaxSearchAll(string keyword, int skip = 0, int take = 20);

        /// <summary>
        /// 删除指定消息的索引
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="messageId">消息ID</param>
        /// <returns>异步任务</returns>
        Task DeleteDocumentAsync(long groupId, long messageId);

        /// <summary>
        /// 检查指定群组的索引是否存在
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <returns>索引是否存在</returns>
        Task<bool> IndexExistsAsync(long groupId);
    }
}
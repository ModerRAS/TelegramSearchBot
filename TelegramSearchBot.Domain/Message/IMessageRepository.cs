using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using Message = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Domain.Message
{
    /// <summary>
    /// Message仓储接口，定义消息数据访问操作
    /// </summary>
    public interface IMessageRepository
    {
        /// <summary>
        /// 根据群组ID获取消息列表
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="startDate">开始日期（可选）</param>
        /// <param name="endDate">结束日期（可选）</param>
        /// <returns>消息列表</returns>
        Task<IEnumerable<Message>> GetMessagesByGroupIdAsync(long groupId, DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// 根据群组ID和消息ID获取特定消息
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="messageId">消息ID</param>
        /// <returns>消息对象，如果不存在则返回null</returns>
        Task<Message> GetMessageByIdAsync(long groupId, long messageId);

        /// <summary>
        /// 添加新消息
        /// </summary>
        /// <param name="message">消息对象</param>
        /// <returns>新消息的ID</returns>
        Task<long> AddMessageAsync(Message message);

        /// <summary>
        /// 搜索消息
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="limit">结果限制数量</param>
        /// <returns>匹配的消息列表</returns>
        Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int limit = 50);

        /// <summary>
        /// 根据用户ID获取消息列表
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="userId">用户ID</param>
        /// <returns>用户的消息列表</returns>
        Task<IEnumerable<Message>> GetMessagesByUserAsync(long groupId, long userId);

        /// <summary>
        /// 删除消息
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="messageId">消息ID</param>
        /// <returns>删除是否成功</returns>
        Task<bool> DeleteMessageAsync(long groupId, long messageId);

        /// <summary>
        /// 更新消息内容
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="newContent">新内容</param>
        /// <returns>更新是否成功</returns>
        Task<bool> UpdateMessageContentAsync(long groupId, long messageId, string newContent);
    }
}
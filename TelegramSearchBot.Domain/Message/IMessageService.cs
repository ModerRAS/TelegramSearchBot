using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Domain.Message;

namespace TelegramSearchBot.Domain.Message
{
    /// <summary>
    /// Message服务接口，定义消息业务逻辑操作
    /// </summary>
    public interface IMessageService
    {
        /// <summary>
        /// 处理传入的消息
        /// </summary>
        /// <param name="messageOption">消息选项</param>
        /// <returns>处理后的消息ID</returns>
        Task<long> ProcessMessageAsync(MessageOption messageOption);

        /// <summary>
        /// 获取群组中的消息列表
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="page">页码</param>
        /// <param name="pageSize">页面大小</param>
        /// <returns>消息列表</returns>
        Task<IEnumerable<Message>> GetGroupMessagesAsync(long groupId, int page = 1, int pageSize = 50);

        /// <summary>
        /// 搜索消息
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="keyword">关键词</param>
        /// <param name="page">页码</param>
        /// <param name="pageSize">页面大小</param>
        /// <returns>搜索结果</returns>
        Task<IEnumerable<Message>> SearchMessagesAsync(long groupId, string keyword, int page = 1, int pageSize = 50);

        /// <summary>
        /// 获取用户消息
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="userId">用户ID</param>
        /// <param name="page">页码</param>
        /// <param name="pageSize">页面大小</param>
        /// <returns>用户消息列表</returns>
        Task<IEnumerable<Message>> GetUserMessagesAsync(long groupId, long userId, int page = 1, int pageSize = 50);

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
        Task<bool> UpdateMessageAsync(long groupId, long messageId, string newContent);
    }
}
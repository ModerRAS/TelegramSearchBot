using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Interface
{
    /// <summary>
    /// 消息发送服务接口
    /// </summary>
    public interface ISendMessageService
    {
        /// <summary>
        /// 发送文本消息
        /// </summary>
        /// <param name="text">消息文本</param>
        /// <param name="chatId">聊天ID</param>
        /// <param name="replyToMessageId">回复的消息ID</param>
        /// <param name="disableNotification">是否静默发送</param>
        /// <returns>发送的消息</returns>
        Task<Message> SendTextMessageAsync(string text, long chatId, int replyToMessageId = 0, bool disableNotification = false);

        /// <summary>
        /// 分割并发送长文本消息
        /// </summary>
        /// <param name="text">消息文本</param>
        /// <param name="chatId">聊天ID</param>
        /// <param name="replyToMessageId">回复的消息ID</param>
        /// <returns>异步任务</returns>
        Task SplitAndSendTextMessage(string text, long chatId, int replyToMessageId = 0);

        /// <summary>
        /// 发送带按钮的消息
        /// </summary>
        /// <param name="text">消息文本</param>
        /// <param name="chatId">聊天ID</param>
        /// <param name="replyToMessageId">回复的消息ID</param>
        /// <param name="buttons">按钮列表</param>
        /// <returns>发送的消息</returns>
        Task<Message> SendButtonMessageAsync(string text, long chatId, int replyToMessageId = 0, params (string text, string callbackData)[] buttons);

        /// <summary>
        /// 添加任务到消息队列
        /// </summary>
        /// <param name="action">要执行的任务</param>
        /// <param name="isGroup">是否为群组消息</param>
        /// <returns>异步任务</returns>
        Task AddTask(Func<Task> action, bool isGroup);

        /// <summary>
        /// 记录日志
        /// </summary>
        /// <param name="text">日志文本</param>
        /// <returns>异步任务</returns>
        Task Log(string text);

        /// <summary>
        /// 发送图片消息
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="photo">图片文件</param>
        /// <param name="caption">图片说明</param>
        /// <param name="replyToMessageId">回复的消息ID</param>
        /// <param name="disableNotification">是否静默发送</param>
        /// <returns>发送的消息</returns>
        Task<Message> SendPhotoAsync(long chatId, InputFile photo, string caption = null, int replyToMessageId = 0, bool disableNotification = false);

        /// <summary>
        /// 流式发送完整消息
        /// </summary>
        /// <param name="fullMessagesStream">消息流</param>
        /// <param name="chatId">聊天ID</param>
        /// <param name="replyTo">回复的消息ID</param>
        /// <param name="initialPlaceholderContent">初始占位内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送的消息列表</returns>
        Task<List<TelegramSearchBot.Model.Data.Message>> SendFullMessageStream(
            IAsyncEnumerable<string> fullMessagesStream,
            long chatId,
            int replyTo,
            string initialPlaceholderContent = "⏳",
            CancellationToken cancellationToken = default);
    }
}
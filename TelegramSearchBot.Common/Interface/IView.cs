using System.Threading.Tasks;
using System.Collections.Generic;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Interface
{
    /// <summary>
    /// 视图接口 - 扩展版本
    /// 提供消息渲染和显示功能
    /// </summary>
    public interface IView
    {
        /// <summary>
        /// 设置聊天ID
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <returns>视图实例</returns>
        IView WithChatId(long chatId);

        /// <summary>
        /// 设置回复消息ID
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <returns>视图实例</returns>
        IView WithReplyTo(int messageId);

        /// <summary>
        /// 设置文本内容
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <returns>视图实例</returns>
        IView WithText(string text);

        /// <summary>
        /// 设置结果数量
        /// </summary>
        /// <param name="count">结果数量</param>
        /// <returns>视图实例</returns>
        IView WithCount(int count);

        /// <summary>
        /// 设置跳过数量
        /// </summary>
        /// <param name="skip">跳过数量</param>
        /// <returns>视图实例</returns>
        IView WithSkip(int skip);

        /// <summary>
        /// 设置获取数量
        /// </summary>
        /// <param name="take">获取数量</param>
        /// <returns>视图实例</returns>
        IView WithTake(int take);

        /// <summary>
        /// 设置搜索类型
        /// </summary>
        /// <param name="searchType">搜索类型</param>
        /// <returns>视图实例</returns>
        IView WithSearchType(SearchType searchType);

        /// <summary>
        /// 设置消息列表
        /// </summary>
        /// <param name="messages">消息列表</param>
        /// <returns>视图实例</returns>
        IView WithMessages(List<Message> messages);

        /// <summary>
        /// 设置标题
        /// </summary>
        /// <param name="title">标题</param>
        /// <returns>视图实例</returns>
        IView WithTitle(string title);

        /// <summary>
        /// 设置帮助信息
        /// </summary>
        /// <returns>视图实例</returns>
        IView WithHelp();

        /// <summary>
        /// 禁用通知
        /// </summary>
        /// <param name="disable">是否禁用</param>
        /// <returns>视图实例</returns>
        IView DisableNotification(bool disable = true);

        /// <summary>
        /// 设置消息内容
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns>视图实例</returns>
        IView WithMessage(string message);

        /// <summary>
        /// 设置所有者名称
        /// </summary>
        /// <param name="ownerName">所有者名称</param>
        /// <returns>视图实例</returns>
        IView WithOwnerName(string ownerName);

        /// <summary>
        /// 渲染并发送消息
        /// </summary>
        /// <returns>异步任务</returns>
        Task Render();
    }
}
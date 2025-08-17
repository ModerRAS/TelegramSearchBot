using System.Threading.Tasks;

namespace TelegramSearchBot.Interface
{
    /// <summary>
    /// 管理员服务接口
    /// 提供管理员权限验证和管理功能
    /// </summary>
    public interface IAdminService : IService
    {
        /// <summary>
        /// 检查是否为全局管理员
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>是否为全局管理员</returns>
        bool IsGlobalAdmin(long userId);

        /// <summary>
        /// 检查是否为普通管理员
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>是否为普通管理员</returns>
        Task<bool> IsNormalAdmin(long userId);

        /// <summary>
        /// 执行管理员命令
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="chatId">聊天ID</param>
        /// <param name="command">命令</param>
        /// <returns>执行结果和消息</returns>
        Task<(bool success, string message)> ExecuteAsync(long userId, long chatId, string command);
    }
}
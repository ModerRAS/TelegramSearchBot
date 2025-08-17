namespace TelegramSearchBot.Interface
{
    /// <summary>
    /// 环境配置服务接口
    /// 提供应用程序配置和路径信息
    /// </summary>
    public interface IEnvService
    {
        /// <summary>
        /// 工作目录路径
        /// </summary>
        string WorkDir { get; }

        /// <summary>
        /// 基础URL
        /// </summary>
        string BaseUrl { get; }

        /// <summary>
        /// 是否使用本地API
        /// </summary>
        bool IsLocalAPI { get; }

        /// <summary>
        /// 机器人Token
        /// </summary>
        string BotToken { get; }

        /// <summary>
        /// 管理员ID
        /// </summary>
        long AdminId { get; }
    }
}
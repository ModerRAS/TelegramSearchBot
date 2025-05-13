using System.IO;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// 提供媒体文件存储和检索功能的服务
    /// </summary>
    public interface IMediaStorageService
    {
        /// <summary>
        /// 保存音频文件到对应用户目录
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="fileName">文件名</param>
        /// <param name="fileData">文件数据</param>
        /// <returns>文件保存路径</returns>
        Task<string> SaveAudioAsync(long chatId, int messageId, string fileName, byte[] fileData);

        /// <summary>
        /// 保存视频文件到对应用户目录
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="fileName">文件名</param>
        /// <param name="fileData">文件数据</param>
        /// <returns>文件保存路径</returns>
        Task<string> SaveVideoAsync(long chatId, int messageId, string fileName, byte[] fileData);

        /// <summary>
        /// 保存图片文件到对应用户目录
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="fileName">文件名</param>
        /// <param name="fileData">文件数据</param>
        /// <returns>文件保存路径</returns>
        Task<string> SavePhotoAsync(long chatId, int messageId, string fileName, byte[] fileData);

        /// <summary>
        /// 获取音频文件路径
        /// </summary>
        /// <param name="update">Telegram更新消息</param>
        /// <returns>音频文件路径</returns>
        string GetAudioPath(Update update);

        /// <summary>
        /// 获取视频文件路径
        /// </summary>
        /// <param name="update">Telegram更新消息</param>
        /// <returns>视频文件路径</returns>
        string GetVideoPath(Update update);

        /// <summary>
        /// 获取图片文件路径
        /// </summary>
        /// <param name="update">Telegram更新消息</param>
        /// <returns>图片文件路径</returns>
        string GetPhotoPath(Update update);

        /// <summary>
        /// 获取音频文件数据
        /// </summary>
        /// <param name="update">Telegram更新消息</param>
        /// <returns>音频文件数据</returns>
        Task<byte[]> GetAudioDataAsync(Update update);

        /// <summary>
        /// 获取视频文件数据
        /// </summary>
        /// <param name="update">Telegram更新消息</param>
        /// <returns>视频文件数据</returns>
        Task<byte[]> GetVideoDataAsync(Update update);

        /// <summary>
        /// 获取图片文件数据
        /// </summary>
        /// <param name="update">Telegram更新消息</param>
        /// <returns>图片文件数据</returns>
        Task<byte[]> GetPhotoDataAsync(Update update);

        /// <summary>
        /// 确保媒体目录存在
        /// </summary>
        /// <param name="directoryPath">目录路径</param>
        void EnsureDirectoryExists(string directoryPath);
    }
} 
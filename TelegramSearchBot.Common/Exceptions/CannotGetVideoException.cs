using System;

namespace TelegramSearchBot.Exceptions
{
    /// <summary>
    /// 无法获取视频异常
    /// </summary>
    public class CannotGetVideoException : Exception
    {
        public CannotGetVideoException() : base("无法获取视频文件")
        {
        }

        public CannotGetVideoException(string message) : base(message)
        {
        }

        public CannotGetVideoException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
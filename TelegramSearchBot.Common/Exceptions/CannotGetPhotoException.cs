using System;

namespace TelegramSearchBot.Exceptions
{
    /// <summary>
    /// 无法获取照片异常
    /// </summary>
    public class CannotGetPhotoException : Exception
    {
        public CannotGetPhotoException() : base("无法获取照片文件")
        {
        }

        public CannotGetPhotoException(string message) : base(message)
        {
        }

        public CannotGetPhotoException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
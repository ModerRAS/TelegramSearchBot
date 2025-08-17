using System;

namespace TelegramSearchBot.Exceptions
{
    /// <summary>
    /// 无法获取音频异常
    /// </summary>
    public class CannotGetAudioException : Exception
    {
        public CannotGetAudioException() : base("无法获取音频文件")
        {
        }

        public CannotGetAudioException(string message) : base(message)
        {
        }

        public CannotGetAudioException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
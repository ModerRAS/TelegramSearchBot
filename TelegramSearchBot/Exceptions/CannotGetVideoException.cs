using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Exceptions {
    public class CannotGetVideoException : Exception {
        public CannotGetVideoException() : base("无法获取视频") { }
        public CannotGetVideoException(string message) : base(message) { }
        public CannotGetVideoException(string message, Exception innerException) : base(message, innerException) { }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Exceptions {
    public class CannotGetPhotoException : Exception {
        public CannotGetPhotoException() : base("无法获取图片") { }
        public CannotGetPhotoException(string message) : base(message) { }
        public CannotGetPhotoException(string message, Exception innerException) : base(message, innerException) { }
    }
}

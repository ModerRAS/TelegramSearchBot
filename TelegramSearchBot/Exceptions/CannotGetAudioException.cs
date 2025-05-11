using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Exceptions {
    public class CannotGetAudioException: Exception {
        public CannotGetAudioException() { }
        public CannotGetAudioException(string message) : base(message) { }
        public CannotGetAudioException(string message, Exception innerException) : base(message, innerException) { }
    }
}

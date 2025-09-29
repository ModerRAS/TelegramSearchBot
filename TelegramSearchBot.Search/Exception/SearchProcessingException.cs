using System;

namespace TelegramSearchBot.Search.Exception {
    public class SearchProcessingException : System.Exception {
        public SearchProcessingException() { }
        public SearchProcessingException(string message) : base(message) { }
        public SearchProcessingException(string message, System.Exception inner) : base(message, inner) { }
    }
}

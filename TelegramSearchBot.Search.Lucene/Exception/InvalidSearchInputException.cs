using System;

namespace TelegramSearchBot.Search.Lucene.Exception {
    public class InvalidSearchInputException : System.Exception {
        public InvalidSearchInputException() { }
        public InvalidSearchInputException(string message) : base(message) { }
        public InvalidSearchInputException(string message, System.Exception inner) : base(message, inner) { }
    }
}

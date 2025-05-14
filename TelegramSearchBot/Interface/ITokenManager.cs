using System.Collections.Generic;

namespace TelegramSearchBot.Interface {
    public interface ITokenManager {
        public List<string> ListToken(string TokenType);
        public bool RemoveToken(string TokenType, string Token);
        public void AddToken(string TokenType, string Token);
        public bool CheckToken(string TokenType, string Token);
    }
}

using System.Collections.Generic;

namespace TelegramSearchBot.Intrerface {
    public interface ITokenManager {
        public List<string> ListToken(string TokenType);
        public bool RemoveToken(string TokenType, string Token);
        public void AddToken(string TokenType, string Token);
        public bool CheckToken(string TokenType, string Token);
    }
}

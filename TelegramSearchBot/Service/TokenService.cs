using System;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;

namespace TelegramSearchBot.Service {
    public class TokenService {
        private ITokenManager tokenManager;
        public TokenService(ITokenManager tokenManager) {
            this.tokenManager = tokenManager;
        }
        public string GenerateNewToken() {
            return Guid.NewGuid().ToString().Replace("-", "");
        }
        public async Task<string> ExecuteAsync(string Command) {
            if (Command.Length > 8 && Command.StartsWith("新建Token ")) {
                var TokenType = Command.Substring(8);
                var Token = GenerateNewToken();
                tokenManager.AddToken(TokenType, Token);
                return Token;
            }
            if (Command.Length > 8 && Command.Equals("查看Token ")) {
                var TokenType = Command.Substring(8);
                var tokens = tokenManager.ListToken(TokenType);
                return string.Join("\n", tokens);
            }
            if (Command.Length > 8 && Command.Equals("删除Token ")) {
                var TokenWithType = Command.Substring(8).Split(" ");
                var TokenType = TokenWithType[0];
                var Token = TokenWithType[1];
                var result = tokenManager.RemoveToken(TokenType, Token);
                return result.ToString();
            }
            return string.Empty;
        }
    }
}

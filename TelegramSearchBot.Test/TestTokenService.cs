using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Test {
    public class TestTokenManager : ITokenManager {
        public void AddToken(string TokenType, string Token) {
            throw new NotImplementedException();
        }

        public bool CheckToken(string TokenType, string Token) {
            throw new NotImplementedException();
        }

        public List<string> ListToken(string TokenType) {
            throw new NotImplementedException();
        }

        public bool RemoveToken(string TokenType, string Token) {
            throw new NotImplementedException();
        }
    }
    [TestClass]
    public class TestTokenService {
        TokenService TokenService { get; set; }
        public TestTokenService() {
            var tokenManager = new TestTokenManager();
            TokenService = new TokenService(tokenManager);
        }
        [TestMethod]
        public void TestGenerateToken() {
            Assert.AreEqual(TokenService.GenerateNewToken().Length, 32);
        }
        [TestMethod]
        public void TestListToken() {
        }
        [TestMethod]
        public void TestRemoveToken() {
        }
    }
}
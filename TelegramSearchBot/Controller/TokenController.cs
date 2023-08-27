using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    public class TokenController : IOnUpdate {
        private TokenService tokenService;
        public TokenController(TokenService tokenService) {
            this.tokenService = tokenService;
        }
        public async Task ExecuteAsync(Update e) {
            if (e?.Message?.Chat?.Id < 0) {
                return;
            }
            if (e?.Message?.Chat?.Id != Env.AdminId) {
                return;
            }
            string Command;
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                Command = e.Message.Text;
            } else if (!string.IsNullOrEmpty(e.Message.Caption)) {
                Command = e.Message.Caption;
            } else return;
            await tokenService.ExecuteAsync(Command);
        }
    }
}

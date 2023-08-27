using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    public class TokenController : IOnUpdate {
        private TokenService tokenService;
        private SendMessage Send;
        private ITelegramBotClient botClient;
        public TokenController(ITelegramBotClient botClient, TokenService tokenService, SendMessage Send) {
            this.tokenService = tokenService;
            this.Send = Send;
            this.botClient = botClient;
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
            var result = await tokenService.ExecuteAsync(Command);
            await Send.AddTask(async () => {
                await botClient.SendTextMessageAsync(
            chatId: e.Message.Chat.Id,
            disableNotification: true,
            text: result
            );
            }, false);
        }
    }
}

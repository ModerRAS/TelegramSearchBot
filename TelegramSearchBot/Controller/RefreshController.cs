using System.Threading.Tasks;
using Telegram.Bot.Args;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class RefreshController : IOnMessage {
        private readonly RefreshService refreshService;
        public RefreshController(RefreshService refreshService) {
            this.refreshService = refreshService;
        }

        

        public async Task ExecuteAsync(object sender, MessageEventArgs e) {
            if (e.Message.Chat.Id < 0) {
                return;
            }
            if (e.Message.Chat.Id != Env.AdminId) {
                return;
            }
            string Command;
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                Command = e.Message.Text;
            } else if (!string.IsNullOrEmpty(e.Message.Caption)) {
                Command = e.Message.Caption;
            } else return;
            await refreshService.ExecuteAsync(Command);
        }

    }
}

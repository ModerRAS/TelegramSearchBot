using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    public class AdminController : IOnUpdate {
        public List<Type> Dependencies => new List<Type>();
        public AdminService AdminService { get; set; }
        public SendMessage Send { get; set; }
        public ITelegramBotClient botClient { get; set; }
        public AdminController(ITelegramBotClient botClient, AdminService adminService, SendMessage Send) {
            AdminService = adminService;
            this.Send = Send;
            this.botClient = botClient;
        }

        public async Task ExecuteAsync(Update e) {
            if (e?.Message?.Chat?.Id > 0) {
                return;
            }
            if (e?.Message?.From?.Id != Env.AdminId) {
                return;
            }
            string Command;
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                Command = e.Message.Text;
            } else if (!string.IsNullOrEmpty(e.Message.Caption)) {
                Command = e.Message.Caption;
            } else return;
            var (status, message) = await AdminService.ExecuteAsync(e.Message.From.Id, e.Message.Chat.Id, Command);
            if (status) {
                await Send.AddTask(async () => {
                    await botClient.SendMessage(
                    chatId: e.Message.Chat.Id,
                    text: message,
                    replyParameters: new ReplyParameters() { MessageId = e.Message.MessageId }
                );
                }, e.Message.Chat.Id < 0);
            }
            
        }
    }
}

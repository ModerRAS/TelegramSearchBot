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
    public class CheckBanGroupController : IOnUpdate {
        protected CheckBanGroupService service;
        protected ITelegramBotClient botClient;
        private readonly SendMessage Send;
        public CheckBanGroupController(ITelegramBotClient botClient, CheckBanGroupService service, SendMessage Send) { 
            this.botClient = botClient;
            this.service = service;
            this.Send = Send;
        }
        public async Task ExecuteAsync(Update e) {
            if (e?.Message?.Chat?.Id < 0) {
                long Id = e.Message.Chat.Id;
                if (await service.CheckBlacklist(Id)) {
                    await botClient.LeaveChat(Id);
                    return;
                }
            } else {
                if (e?.Message?.Chat?.Id != Env.AdminId) {
                    return;
                }
                string Command;
                if (!string.IsNullOrEmpty(e.Message.Text)) {
                    Command = e.Message.Text;
                } else if (!string.IsNullOrEmpty(e.Message.Caption)) {
                    Command = e.Message.Caption;
                } else return;
                if (Command.StartsWith("查黑名单")) {
                    await Send.AddTask(async () => {
                        await botClient.SendMessage(
                            chatId: e?.Message?.Chat?.Id,
                            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                            text: await service.GetGroupList()
                            );
                    }, e.Message.Chat.Id < 0);
                } else if (Command.StartsWith("封群 ")) {
                    if (long.TryParse(Command.Replace("封群 ", ""), out long result)) {
                        await service.BanGroup(result);
                        await botClient.LeaveChat(result);
                    }
                } else if (Command.StartsWith("解封群 ")) {
                    if (long.TryParse(Command.Replace("解封群 ", ""), out long result)) {
                        await service.UnBanGroup(result);
                    }
                }
            }
            
        }
    }
}

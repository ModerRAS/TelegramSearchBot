using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Manage;

namespace TelegramSearchBot.Controller.Manage {
    public class EditLLMConfController : IOnUpdate {
        protected readonly AdminService AdminService;
        protected readonly EditLLMConfService EditLLMConfService;
        public SendMessageService Send { get; set; }
        public ITelegramBotClient botClient { get; set; }
        public EditLLMConfController(ITelegramBotClient botClient, SendMessageService Send, AdminService AdminService, EditLLMConfService EditLLMConfService) {
            this.AdminService = AdminService;
            this.EditLLMConfService = EditLLMConfService;
            this.botClient = botClient;
            this.Send = Send;
        }
        public List<Type> Dependencies => new List<Type>();

        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            if (e?.Message?.Chat?.Id < 0) {
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

            var (status, message) = await EditLLMConfService.ExecuteAsync(Command, e.Message.Chat.Id);
            if (status)
            {
                await Send.SplitAndSendTextMessage(message, e.Message.Chat, e.Message.MessageId);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.View;

namespace TelegramSearchBot.Controller.Manage {
    public class EditLLMConfController : IOnUpdate {
        protected readonly AdminService AdminService;
        protected readonly EditLLMConfService EditLLMConfService;
        protected readonly EditLLMConfView EditLLMConfView;
        public ITelegramBotClient botClient { get; set; }
        public EditLLMConfController(
            ITelegramBotClient botClient,
            AdminService AdminService,
            EditLLMConfService EditLLMConfService,
            EditLLMConfView EditLLMConfView) {
            this.AdminService = AdminService;
            this.EditLLMConfService = EditLLMConfService;
            this.EditLLMConfView = EditLLMConfView;
            this.botClient = botClient;
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
            if (status) {
                await EditLLMConfView
                    .WithChatId(e.Message.Chat.Id)
                    .WithReplyTo(e.Message.MessageId)
                    .WithMessage(message)
                    .Render();
            }
        }
    }
}

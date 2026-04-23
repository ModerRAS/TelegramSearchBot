using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.View;

namespace TelegramSearchBot.Controller.Manage {
    public class EditOCRConfController : IOnUpdate {
        protected readonly AdminService AdminService;
        protected readonly EditOCRConfService EditOCRConfService;
        protected readonly EditOCRConfView EditOCRConfView;
        public ITelegramBotClient botClient { get; set; }
        public EditOCRConfController(
            ITelegramBotClient botClient,
            AdminService AdminService,
            EditOCRConfService EditOCRConfService,
            EditOCRConfView EditOCRConfView) {
            this.AdminService = AdminService;
            this.EditOCRConfService = EditOCRConfService;
            this.EditOCRConfView = EditOCRConfView;
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

            var (status, message) = await EditOCRConfService.ExecuteAsync(Command, e.Message.Chat.Id);
            if (status) {
                var replyMarkup = await EditOCRConfService.GetReplyMarkupAsync(e.Message.Chat.Id);
                await EditOCRConfView
                    .WithChatId(e.Message.Chat.Id)
                    .WithReplyTo(e.Message.MessageId)
                    .WithMessage(message)
                    .WithReplyMarkup(replyMarkup)
                    .Render();
            }
        }
    }
}

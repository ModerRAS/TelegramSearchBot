using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using TelegramSearchBot.Common;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.View;

namespace TelegramSearchBot.Controller.Manage {
    public class EditMcpConfController : IOnUpdate {
        protected readonly EditMcpConfService EditMcpConfService;
        protected readonly EditMcpConfView EditMcpConfView;
        public ITelegramBotClient botClient { get; set; }

        public EditMcpConfController(
            ITelegramBotClient botClient,
            EditMcpConfService EditMcpConfService,
            EditMcpConfView EditMcpConfView) {
            this.EditMcpConfService = EditMcpConfService;
            this.EditMcpConfView = EditMcpConfView;
            this.botClient = botClient;
        }

        public List<Type> Dependencies => new List<Type>();

        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            // Only in private chats (positive chat IDs)
            if (e?.Message?.Chat?.Id < 0) {
                return;
            }
            // Admin only
            if (e?.Message?.From?.Id != Env.AdminId) {
                return;
            }
            string Command;
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                Command = e.Message.Text;
            } else if (!string.IsNullOrEmpty(e.Message.Caption)) {
                Command = e.Message.Caption;
            } else return;

            var (status, message) = await EditMcpConfService.ExecuteAsync(Command, e.Message.Chat.Id);
            if (status) {
                await EditMcpConfView
                    .WithChatId(e.Message.Chat.Id)
                    .WithReplyTo(e.Message.MessageId)
                    .WithMessage(message)
                    .Render();
            }
        }
    }
}

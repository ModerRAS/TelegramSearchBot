using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using TelegramSearchBot.Common;
using TelegramSearchBot.Core.Interface.Controller;
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Service.Manage;

namespace TelegramSearchBot.Controller.Manage {
    public class RefreshController : IOnUpdate {
        private readonly RefreshService refreshService;
        public List<Type> Dependencies => new List<Type>();
        public RefreshController(RefreshService refreshService) {
            this.refreshService = refreshService;
        }



        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
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
            await refreshService.ExecuteAsync(Command);
        }

    }
}

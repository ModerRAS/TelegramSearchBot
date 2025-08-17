using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.BotAPI;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.Common;
using TelegramSearchBot.Common.Model;

namespace TelegramSearchBot.Controller.Manage {
    public class AdminController : IOnUpdate, IAdminController
    {
        public List<Type> Dependencies => new List<Type>();
        public AdminService AdminService { get; set; }
        public ISendMessageService Send { get; set; }
        public ITelegramBotClient botClient { get; set; }
        public AdminController(ITelegramBotClient botClient, AdminService adminService, ISendMessageService Send)
        {
            AdminService = adminService;
            this.Send = Send;
            this.botClient = botClient;
        }

        public async Task ExecuteAsync(PipelineContext p)
        {
            var e = p.Update;
            if (e?.Message?.Chat?.Id > 0)
            {
                return;
            }
            if (e?.Message?.From?.Id != Env.AdminId)
            {
                return;
            }
            string Command;
            if (!string.IsNullOrEmpty(e.Message.Text))
            {
                Command = e.Message.Text;
            }
            else if (!string.IsNullOrEmpty(e.Message.Caption))
            {
                Command = e.Message.Caption;
            }
            else return;
            var (status, message) = await AdminService.ExecuteAsync(e.Message.From.Id, e.Message.Chat.Id, Command);
            if (status)
            {
                await Send.SplitAndSendTextMessage(message, e.Message.Chat.Id, e.Message.MessageId);
            }

        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Args;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Controller {
    class LoanController : IOnMessage {
        LoanService loanService { get; set; }
        public LoanController(LoanService loanService) {
            this.loanService = loanService;
        }
        public async Task ExecuteAsync(object sender, MessageEventArgs e) {
            if (e.Message.Chat.Id > 0) {
                return;
            }
            string ToAdd;
            if (!string.IsNullOrEmpty(e.Message.Text)) {
                ToAdd = e.Message.Text;
            } else if (!string.IsNullOrEmpty(e.Message.Caption)) {
                ToAdd = e.Message.Caption;
            } else return;
            if (ToAdd.Length > 3 && ToAdd.Substring(0, 3).Equals("搜索 ")) {
                return;
            }

            await loanService.ExecuteAsync(new MessageOption {
                ChatId = e.Message.Chat.Id,
                MessageId = e.Message.MessageId,
                UserId = e.Message.From.Id,
                Content = ToAdd
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Args;

namespace TelegramSearchBot.Intrerface {
    public abstract class IOnMessageEdited {
        protected ITelegramBotClient botClient;
        protected IOnMessageEdited(ITelegramBotClient botClient) {
            this.botClient = botClient;
            botClient.OnMessageEdited += ExecuteAsync;

        }
        protected abstract void ExecuteAsync(object sender, MessageEventArgs e);
    }
}

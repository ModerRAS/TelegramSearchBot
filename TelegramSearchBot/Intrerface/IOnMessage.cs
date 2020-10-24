using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Args;

namespace TelegramSearchBot.Intrerface {
    public abstract class IOnMessage {
        protected ITelegramBotClient botClient;
        protected IOnMessage(ITelegramBotClient botClient) {
            this.botClient = botClient;
            botClient.OnMessage += ExecuteAsync;
            botClient.OnMessageEdited += ExecuteAsync;

        }
        protected abstract void ExecuteAsync(object sender, MessageEventArgs e);
    }
}

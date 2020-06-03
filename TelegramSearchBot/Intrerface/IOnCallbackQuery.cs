using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Args;

namespace TelegramSearchBot.Intrerface {
    public abstract class IOnCallbackQuery {
        protected ITelegramBotClient botClient;
        protected IOnCallbackQuery(ITelegramBotClient botClient) {
            this.botClient = botClient;
            botClient.OnCallbackQuery += ExecuteAsync;
        }

        protected abstract void ExecuteAsync(object sender, CallbackQueryEventArgs e);
    }
}

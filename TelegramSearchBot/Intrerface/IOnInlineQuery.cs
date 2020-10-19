using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Args;

namespace TelegramSearchBot.Intrerface {
    public abstract class IOnInlineQuery {
        protected ITelegramBotClient botClient;
        protected IOnInlineQuery(ITelegramBotClient botClient) {
            this.botClient = botClient;
            botClient.OnInlineQuery += ExecuteAsync;
        }

        protected abstract void ExecuteAsync(object sender, InlineQueryEventArgs e);
    }
}

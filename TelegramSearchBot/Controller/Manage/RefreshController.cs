using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot.Args;
using MediatR;
using System.Threading;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model.Notifications;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Service.Manage;

namespace TelegramSearchBot.Controller.Manage
{
    class RefreshController : INotificationHandler<TelegramUpdateReceivedNotification>
    {
        private readonly RefreshService _refreshService;
        // public List<Type> Dependencies => new List<Type>(); // Obsolete

        public RefreshController(RefreshService refreshService)
        {
            _refreshService = refreshService;
        }

        public async Task Handle(TelegramUpdateReceivedNotification notification, CancellationToken cancellationToken)
        {
            var update = notification.Update;

            if (update.Type != UpdateType.Message || update.Message == null)
            {
                return;
            }

            var message = update.Message;
            var chat = message.Chat;
            var from = message.From;

            // Only process commands from Admin in a private chat
            if (chat.Type == ChatType.Group || chat.Type == ChatType.Supergroup || chat.Type == ChatType.Channel) {
                return; // Not a private chat
            }
            // The original logic checked chat.Id == Env.AdminId. If admin can only interact in private chat, this is fine.
            // If admin could issue these from other private chats, then from.Id == Env.AdminId is the key.
            // Assuming private chat with admin:
            if (chat.Id != Env.AdminId || from?.Id != Env.AdminId) { 
                return;
            }

            string commandText;
            if (!string.IsNullOrEmpty(message.Text))
            {
                commandText = message.Text;
            }
            else if (!string.IsNullOrEmpty(message.Caption))
            {
                commandText = message.Caption;
            }
            else return; // No command text
            
            // Assuming RefreshService.ExecuteAsync handles specific command parsing
            await _refreshService.ExecuteAsync(commandText);
        }
    }
}

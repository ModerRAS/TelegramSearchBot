using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using MediatR;
using System.Threading;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model.Notifications;
using Telegram.Bot.Types;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.Manage;

namespace TelegramSearchBot.Controller.Manage
{
    public class AdminController : INotificationHandler<TelegramUpdateReceivedNotification>
    {
        // public List<Type> Dependencies => new List<Type>(); // Obsolete
        private readonly AdminService _adminService;
        private readonly SendMessage _sendManager; // Renamed for consistency
        private readonly ITelegramBotClient _botClient;

        public AdminController(ITelegramBotClient botClient, AdminService adminService, SendMessage sendManager)
        {
            _adminService = adminService;
            _sendManager = sendManager;
            _botClient = botClient;
        }

        public async Task Handle(TelegramUpdateReceivedNotification notification, CancellationToken cancellationToken)
        {
            var update = notification.Update;

            if (update.Type != UpdateType.Message || update.Message == null)
            {
                return;
            }

            var message = update.Message;

            // Admin commands are typically in group chats and from the admin user
            if (message.Chat?.Id > 0) // Not a group chat (or supergroup)
            {
                return;
            }
            if (message.From?.Id != Env.AdminId) // Not from admin
            {
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

            var (status, responseMessage) = await _adminService.ExecuteAsync(message.From.Id, message.Chat.Id, commandText);
            
            if (status && !string.IsNullOrEmpty(responseMessage))
            {
                await _sendManager.AddTask(async () =>
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: responseMessage,
                        replyParameters: new ReplyParameters() { MessageId = message.MessageId },
                        cancellationToken: cancellationToken
                    );
                }, message.Chat.Type != ChatType.Private); // Determine if it's a group for rate limiting
            }
        }
    }
}

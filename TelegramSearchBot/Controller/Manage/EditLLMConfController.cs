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

namespace TelegramSearchBot.Controller.Manage {
    public class EditLLMConfController : INotificationHandler<TelegramUpdateReceivedNotification> {
        private readonly AdminService _adminService; // Kept, though not used in current logic
        private readonly EditLLMConfService _editLLMConfService;
        private readonly SendMessage _sendManager; // Renamed Send
        private readonly ITelegramBotClient _botClient;
        
        // public List<Type> Dependencies => new List<Type>(); // Obsolete

        public EditLLMConfController(ITelegramBotClient botClient, SendMessage sendManager, AdminService adminService, EditLLMConfService editLLMConfService) {
            _adminService = adminService;
            _editLLMConfService = editLLMConfService;
            _botClient = botClient;
            _sendManager = sendManager;
        }

        public async Task Handle(TelegramUpdateReceivedNotification notification, CancellationToken cancellationToken) {
            var update = notification.Update;

            if (update.Type != UpdateType.Message || update.Message == null) {
                return;
            }

            var message = update.Message;
            var chat = message.Chat;
            var from = message.From;

            // Only process commands from Admin in a private chat
            if (chat.Type == ChatType.Group || chat.Type == ChatType.Supergroup || chat.Type == ChatType.Channel) {
                return; // Not a private chat with the bot
            }
            if (from?.Id != Env.AdminId) { // If not admin, return
                return;
            }
            // At this point, chat.Id should be Env.AdminId if it's a private chat with admin

            string commandText;
            if (!string.IsNullOrEmpty(message.Text)) {
                commandText = message.Text;
            } else if (!string.IsNullOrEmpty(message.Caption)) {
                commandText = message.Caption;
            } else return; // No command text

            var (status, responseMessage) = await _editLLMConfService.ExecuteAsync(commandText, chat.Id);
            
            if (status && !string.IsNullOrEmpty(responseMessage)) {
                await _sendManager.AddTask(async () => {
                    await _botClient.SendTextMessageAsync(
                        chatId: chat.Id,
                        text: responseMessage,
                        replyParameters: new ReplyParameters() { MessageId = message.MessageId },
                        cancellationToken: cancellationToken
                    );
                }, false); // false for IsGroup as it's a private chat
            }
        }
    }
}

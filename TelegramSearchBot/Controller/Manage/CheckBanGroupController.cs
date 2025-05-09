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
    public class CheckBanGroupController : INotificationHandler<TelegramUpdateReceivedNotification>
    {
        private readonly CheckBanGroupService _service;
        private readonly ITelegramBotClient _botClient;
        private readonly SendMessage _sendManager; // Renamed Send

        // public List<Type> Dependencies => new List<Type>(); // Obsolete

        public CheckBanGroupController(ITelegramBotClient botClient, CheckBanGroupService service, SendMessage sendManager)
        {
            _botClient = botClient;
            _service = service;
            _sendManager = sendManager;
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

            // Part 1: Auto-leave blacklisted group (applies to any group message)
            if (chat.Type == ChatType.Group || chat.Type == ChatType.Supergroup)
            {
                if (await _service.CheckBlacklist(chat.Id))
                {
                    await _botClient.LeaveChatAsync(chat.Id, cancellationToken);
                    // If bot leaves, no further processing for this message in this handler.
                    // Other handlers might still process if they don't depend on bot being in chat.
                    return; 
                }
            }

            // Part 2: Admin commands (only if message is from admin in a private chat with the bot)
            // The original logic checked e.Message.Chat.Id == Env.AdminId, which implies a private chat with the admin.
            // And e.Message.From.Id was implicitly the admin too.
            if (chat.Id == Env.AdminId && from?.Id == Env.AdminId)
            {
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

                if (commandText.StartsWith("查黑名单"))
                {
                    await _sendManager.AddTask(async () =>
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: chat.Id, // Send to admin
                            parseMode: ParseMode.Markdown,
                            text: await _service.GetGroupList(),
                            cancellationToken: cancellationToken
                            );
                    }, false); // false for IsGroup as it's a private chat with admin
                }
                else if (commandText.StartsWith("封群 "))
                {
                    if (long.TryParse(commandText.Replace("封群 ", ""), out long groupIdToBan))
                    {
                        await _service.BanGroup(groupIdToBan);
                        await _botClient.LeaveChatAsync(groupIdToBan, cancellationToken);
                        // Optionally send confirmation to admin
                        await _sendManager.AddTask(async () => {
                            await _botClient.SendTextMessageAsync(chat.Id, $"已尝试封禁群组 {groupIdToBan} 并退出。", cancellationToken: cancellationToken);
                        }, false);
                    }
                }
                else if (commandText.StartsWith("解封群 "))
                {
                    if (long.TryParse(commandText.Replace("解封群 ", ""), out long groupIdToUnban))
                    {
                        await _service.UnBanGroup(groupIdToUnban);
                         // Optionally send confirmation to admin
                        await _sendManager.AddTask(async () => {
                            await _botClient.SendTextMessageAsync(chat.Id, $"已解封群组 {groupIdToUnban}。", cancellationToken: cancellationToken);
                        }, false);
                    }
                }
            }
        }
    }
}

using TelegramSearchBot.Intrerface;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using System;
using System.Collections.Generic;
using System.Linq; // Added for Enumerable.Any
using TelegramSearchBot.Service.Storage;
using MediatR; // Added for IMediator
using TelegramSearchBot.Model.Notifications;
using Telegram.Bot.Types.Enums;
using System.Threading; // Added for CancellationToken

namespace TelegramSearchBot.Controller.Storage
{
    // Changed from IOnUpdate to INotificationHandler<TelegramUpdateReceivedNotification>
    class MessageController : INotificationHandler<TelegramUpdateReceivedNotification>
    {
        private readonly MessageService _messageService;
        private readonly IMediator _mediator;
        // public List<Type> Dependencies => new List<Type>(); // This might be obsolete with MediatR

        public MessageController(
            MessageService messageService,
            IMediator mediator)
        {
            _messageService = messageService;
            _mediator = mediator;
        }

        public async Task Handle(TelegramUpdateReceivedNotification notification, CancellationToken cancellationToken)
        {
            var update = notification.Update;

            // Only handle Message updates
            if (update.Type != UpdateType.Message)
            {
                return;
            }

            var message = update.Message;
            if (message == null) return;

            // Store the message first
            await StoreMessageAsync(update); // Pass the full Update object

            string? messageText = message.Text ?? message.Caption;

            if (string.IsNullOrWhiteSpace(messageText))
            {
                return;
            }
            
            // Avoid publishing for "搜索 " commands
            if (messageText.Length > 3 && messageText.StartsWith("搜索 "))
            {
                return;
            }

            // Publish a secondary notification for URL processing
            await _mediator.Publish(new TextMessageReceivedNotification(
                messageText,
                message.Chat.Id,
                message.MessageId,
                message.Chat.Type
            ), cancellationToken);
        }
        
        // StoreMessageAsync now takes Update directly
        private async Task StoreMessageAsync(Update update)
        {
            // Ensure we are dealing with a message
            if (update.Message == null) return;
            var message = update.Message;

            string ToAdd;
            if (!string.IsNullOrEmpty(message.Text))
            {
                ToAdd = message.Text;
            }
            else if (!string.IsNullOrEmpty(message.Caption))
            {
                ToAdd = message.Caption;
            }
            else return;

            await _messageService.ExecuteAsync(new MessageOption
            {
                ChatId = message.Chat.Id,
                MessageId = message.MessageId,
                UserId = message.From.Id,
                Content = ToAdd,
                DateTime = message.Date,
                User = message.From,
                ReplyTo = message.ReplyToMessage?.Id ?? 0,
                Chat = message.Chat,
            });
        }
    }
}

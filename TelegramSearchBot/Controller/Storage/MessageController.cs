using TelegramSearchBot.Intrerface;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using System;
using System.Collections.Generic;
using System.Linq; // Added for Enumerable.Any
using TelegramSearchBot.Service.Storage;
using MediatR; // Added for IMediator
using TelegramSearchBot.Model.Notifications; // Added for TextMessageReceivedNotification
using Telegram.Bot.Types.Enums; 

namespace TelegramSearchBot.Controller.Storage
{
    class MessageController : IOnUpdate
    {
        private readonly MessageService _messageService;
        private readonly IMediator _mediator;
        public List<Type> Dependencies => new List<Type>();

        public MessageController(
            MessageService messageService,
            IMediator mediator)
        {
            _messageService = messageService;
            _mediator = mediator;
        }

        public async Task ExecuteAsync(Update e)
        {
            // Store the message first
            await StoreMessageAsync(e);

            string? messageText = e?.Message?.Text ?? e?.Message?.Caption;

            if (string.IsNullOrWhiteSpace(messageText))
            {
                return;
            }
            
            if (messageText.Length > 3 && messageText.StartsWith("搜索 "))
            {
                return;
            }

            if (e.Message != null) 
            {
                 // Pass the full Message object to the constructor
                 await _mediator.Publish(new TextMessageReceivedNotification(e.Message));
            }
        }
        
        private async Task StoreMessageAsync(Update e)
        {
            string ToAdd;
            if (!string.IsNullOrEmpty(e?.Message?.Text))
            {
                ToAdd = e.Message.Text;
            }
            else if (!string.IsNullOrEmpty(e?.Message?.Caption))
            {
                ToAdd = e.Message.Caption;
            }
            else return;

            await _messageService.ExecuteAsync(new MessageOption
            {
                ChatId = e.Message.Chat.Id,
                MessageId = e.Message.MessageId,
                UserId = e.Message.From.Id,
                Content = ToAdd,
                DateTime = e.Message.Date,
                User = e.Message.From,
                ReplyTo = e.Message.ReplyToMessage?.Id ?? 0,
                Chat = e.Message.Chat,
            });
        }
    }
}

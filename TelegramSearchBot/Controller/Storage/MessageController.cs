﻿using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using System;
using System.Collections.Generic;
using System.Linq; // Added for Enumerable.Any
using TelegramSearchBot.Service.Storage;
using MediatR; // Added for IMediator
using TelegramSearchBot.Model.Notifications; // Added for TextMessageReceivedNotification
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Interface.Controller;

namespace TelegramSearchBot.Controller.Storage {
    public class MessageController : IOnUpdate
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

        public async Task ExecuteAsync(PipelineContext p) {
            var e = p.Update;
            string ToAdd = e?.Message?.Text ?? e?.Message?.Caption ?? string.Empty;
            if (e.CallbackQuery != null) { 
                p.BotMessageType = BotMessageType.CallbackQuery;
                return;
            } else if (e.Message != null) { 
                p.BotMessageType = BotMessageType.Message;
            } else {
                p.BotMessageType = BotMessageType.Unknown;
                return;
            }
            p.MessageDataId = await _messageService.ExecuteAsync(new MessageOption {
                ChatId = e.Message.Chat.Id,
                MessageId = e.Message.MessageId,
                UserId = e.Message.From.Id,
                Content = ToAdd,
                DateTime = e.Message.Date,
                User = e.Message.From,
                ReplyTo = e.Message.ReplyToMessage?.Id ?? 0,
                Chat = e.Message.Chat,
            });
            p.ProcessingResults.Add(ToAdd);
        }
        
    }
}

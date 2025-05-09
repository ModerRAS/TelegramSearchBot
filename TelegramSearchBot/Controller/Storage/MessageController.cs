using TelegramSearchBot.Intrerface;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using System;
using System.Collections.Generic;
using System.Linq; // Added for Enumerable.Any
using TelegramSearchBot.Service.Storage;
using TelegramSearchBot.Service.Common; // Added for IUrlProcessingService
using TelegramSearchBot.Manager; // Added for SendMessage
using Telegram.Bot; // Added for ITelegramBotClient
using Telegram.Bot.Types.Enums; // Added for ChatType and ParseMode

namespace TelegramSearchBot.Controller.Storage
{
    class MessageController : IOnUpdate
    {
        private readonly MessageService _messageService;
        private readonly UrlProcessingService _urlProcessingService; // Changed from IUrlProcessingService
        private readonly SendMessage _sendMessage;
        private readonly ITelegramBotClient _botClient;
        public List<Type> Dependencies => new List<Type>();

        public MessageController(
            MessageService messageService,
            UrlProcessingService urlProcessingService, // Changed from IUrlProcessingService
            SendMessage sendMessage,
            ITelegramBotClient botClient)
        {
            _messageService = messageService;
            _urlProcessingService = urlProcessingService; // Assign UrlProcessingService
            _sendMessage = sendMessage;
            _botClient = botClient;
        }

        public async Task ExecuteAsync(Update e)
        {
            // First, let the original message storage logic run
            // This ensures that the message is saved before we process URLs
            // and potentially reply, avoiding race conditions or missed messages.
            await StoreMessageAsync(e);

            // Now, process URLs if any
            string? messageText = e?.Message?.Text ?? e?.Message?.Caption;

            if (string.IsNullOrWhiteSpace(messageText))
            {
                return;
            }

            // Avoid processing commands like "搜索 "
            if (messageText.Length > 3 && messageText.Substring(0, 3).Equals("搜索 "))
            {
                return;
            }

            var processedUrls = await _urlProcessingService.ProcessUrlsInTextAsync(messageText);

            if (processedUrls != null && processedUrls.Any())
            {
                var replyText = "检测到链接，处理结果如下：\n" + string.Join("\n", processedUrls);
                
                // Use _sendMessage.AddTask with the correct parameters
                await _sendMessage.AddTask(async () =>
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: e.Message.Chat.Id,
                        text: replyText,
                        parseMode: ParseMode.Html, // Using the imported ParseMode
                        replyParameters: new ReplyParameters { MessageId = e.Message.MessageId } 
                    );
                }, e.Message.Chat.Type != ChatType.Private); // IsGroup is true if not a private chat
            }
        }

        // Extracted original message storing logic into a separate method for clarity
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

            // The "搜索 " check is now part of the main ExecuteAsync, 
            // but if it were specific to storing, it would remain here.
            // For now, we assume StoreMessageAsync stores all relevant messages
            // and filtering happens before calling this or before URL processing.

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

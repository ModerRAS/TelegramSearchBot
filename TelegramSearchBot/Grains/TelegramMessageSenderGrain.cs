using Orleans;
using Serilog;
using System;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types; // For ReplyParameters
using Telegram.Bot.Types.Enums; // For ParseMode if needed later
using TelegramSearchBot.Interfaces;

namespace TelegramSearchBot.Grains
{
    // Grain implementation
    public class TelegramMessageSenderGrain : Grain, ITelegramMessageSenderGrain
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger _logger;

        public TelegramMessageSenderGrain(ITelegramBotClient botClient)
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _logger = Log.ForContext<TelegramMessageSenderGrain>();
        }

        public async Task SendMessageAsync(TelegramMessageToSend message)
        {
            if (message == null)
            {
                _logger.Warning("SendMessageAsync called with null message.");
                return;
            }

            try
            {
                _logger.Information("Attempting to send message to ChatId {ChatId}: {Text}", message.ChatId, message.Text);
                ReplyParameters replyParams = null;
                if (message.ReplyToMessageId.HasValue && message.ReplyToMessageId.Value != 0) // Ensure ReplyToMessageId is valid
                {
                    replyParams = new ReplyParameters { MessageId = message.ReplyToMessageId.Value };
                }

                await _botClient.SendTextMessageAsync(
                    chatId: message.ChatId,
                    text: message.Text,
                    replyParameters: replyParams
                    // Consider adding ParseMode, disableWebPagePreview, ReplyMarkup etc. 
                    // from message object if they are added to TelegramMessageToSend
                );
                _logger.Information("Message successfully sent to ChatId {ChatId}", message.ChatId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending Telegram message to ChatId {ChatId}", message.ChatId);
                // Optionally, rethrow or handle specific exceptions if needed by callers
                // For now, just logging the error.
            }
        }
    }
}

using MediatR;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Service.Common;

namespace TelegramSearchBot.Handler
{
    public class UrlProcessingNotificationHandler : INotificationHandler<TextMessageReceivedNotification>
    {
        private readonly UrlProcessingService _urlProcessingService;
        private readonly SendMessage _sendMessage;
        private readonly ITelegramBotClient _botClient;

        public UrlProcessingNotificationHandler(
            UrlProcessingService urlProcessingService,
            SendMessage sendMessage,
            ITelegramBotClient botClient)
        {
            _urlProcessingService = urlProcessingService;
            _sendMessage = sendMessage;
            _botClient = botClient;
        }
        
        public async Task Handle(TextMessageReceivedNotification notification, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(notification.Text))
            {
                return;
            }

            // Avoid processing "搜索 " commands here, as they are handled elsewhere or not relevant for URL processing.
            if (notification.Text.Length > 3 && notification.Text.StartsWith("搜索 "))
            {
                return;
            }

            var processedUrls = await _urlProcessingService.ProcessUrlsInTextAsync(notification.Text);

            if (processedUrls != null && processedUrls.Any())
            {
                var replyText = "检测到链接，处理结果如下：\n" + string.Join("\n", processedUrls);
                
                await _sendMessage.AddTask(async () =>
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: notification.ChatId,
                        text: replyText,
                        parseMode: ParseMode.Html,
                        replyParameters: new Telegram.Bot.Types.ReplyParameters { MessageId = notification.MessageId },
                        cancellationToken: cancellationToken
                    );
                }, notification.ChatType != ChatType.Private);
            }
        }
    }
}

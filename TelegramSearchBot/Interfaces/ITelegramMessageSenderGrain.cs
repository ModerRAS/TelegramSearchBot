using Orleans;
using System.Threading.Tasks;
using Telegram.Bot.Types.Enums; // Added for ParseMode
using Telegram.Bot.Types.ReplyMarkups; 

namespace TelegramSearchBot.Interfaces
{
    // Define a simple message model for sending
    public class TelegramMessageToSend
    {
        public long ChatId { get; set; }
        public string Text { get; set; }
        public int? ReplyToMessageId { get; set; }
        public IReplyMarkup ReplyMarkup { get; set; }
        public ParseMode? ParseMode { get; set; } // Added for specifying message parsing mode
        public bool? DisableWebPagePreview { get; set; } // Added to control web page preview
    }

    /// <summary>
    /// Grain interface responsible for sending messages via the Telegram Bot API.
    /// This acts as a centralized sender for other grains.
    /// </summary>
    public interface ITelegramMessageSenderGrain : IGrainWithIntegerKey // Could be a stateless worker, or keyed if needed (e.g. by BotId if multi-bot)
    {
        /// <summary>
        /// Sends a message and returns the MessageId of the sent message if successful.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>The MessageId of the sent message, or null if sending failed or ID is not available.</returns>
        Task<int?> SendMessageAsync(TelegramMessageToSend message);
        
        /// <summary>
        /// Deletes a message.
        /// </summary>
        /// <param name="chatId">Chat ID.</param>
        /// <param name="messageId">Message ID to delete.</param>
        Task DeleteMessageAsync(long chatId, int messageId);

        /// <summary>
        /// 编辑指定消息的文本和可选的ReplyMarkup。
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="messageId">要编辑的消息ID</param>
        /// <param name="newText">新文本</param>
        /// <param name="replyMarkup">可选的新内联键盘</param>
        Task<bool> EditMessageTextAsync(long chatId, int messageId, string newText, IReplyMarkup replyMarkup = null);

        /// <summary>
        /// 编辑指定消息的ReplyMarkup（仅更新按钮，不改动文本）。
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="messageId">要编辑的消息ID</param>
        /// <param name="replyMarkup">新内联键盘</param>
        Task<bool> EditMessageReplyMarkupAsync(long chatId, int messageId, IReplyMarkup replyMarkup);

        /// <summary>
        /// 发送图片。
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="photoBytes">图片二进制</param>
        /// <param name="caption">说明</param>
        /// <param name="replyToMessageId">回复消息ID</param>
        /// <param name="replyMarkup">内联键盘</param>
        Task<int?> SendPhotoAsync(long chatId, byte[] photoBytes, string caption = null, int? replyToMessageId = null, IReplyMarkup replyMarkup = null);

        /// <summary>
        /// 发送文件。
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="fileBytes">文件二进制</param>
        /// <param name="fileName">文件名</param>
        /// <param name="caption">说明</param>
        /// <param name="replyToMessageId">回复消息ID</param>
        /// <param name="replyMarkup">内联键盘</param>
        Task<int?> SendDocumentAsync(long chatId, byte[] fileBytes, string fileName, string caption = null, int? replyToMessageId = null, IReplyMarkup replyMarkup = null);

        /// <summary>
        /// 发送视频。
        /// </summary>
        /// <param name="chatId">聊天ID</param>
        /// <param name="videoBytes">视频二进制</param>
        /// <param name="caption">说明</param>
        /// <param name="replyToMessageId">回复消息ID</param>
        /// <param name="replyMarkup">内联键盘</param>
        Task<int?> SendVideoAsync(long chatId, byte[] videoBytes, string caption = null, int? replyToMessageId = null, IReplyMarkup replyMarkup = null);
    }
}

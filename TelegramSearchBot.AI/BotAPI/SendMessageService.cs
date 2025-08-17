using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using Markdig;
using Telegram.Bot.Exceptions;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.Web;
using System.Threading;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Service.BotAPI
{
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public partial class SendMessageService : ISendMessageService
    {
        #region Fields and Constructor
        public string ServiceName => "SendMessageService";
        private readonly ITelegramBotClient botClient;
        private readonly ILogger<SendMessageService> logger;
        private readonly ISendMessageService sendMessageService;

        public SendMessageService(ITelegramBotClient botClient, ILogger<SendMessageService> logger, ISendMessageService sendMessageService)
        {
            this.botClient = botClient;
            this.logger = logger;
            this.sendMessageService = sendMessageService;
        }
        #endregion




        #region Fallback and Formatting Helpers
        public async Task TrySendMessageWithFallback(long chatId, int messageId, string originalMarkdownText, ParseMode preferredParseMode, bool isGroup, int replyToMessageId, string initialContentForNewMessage, bool isEdit)
        {
            string textToSend = originalMarkdownText;
            ParseMode currentParseMode = preferredParseMode;

            if (preferredParseMode == ParseMode.Html)
            {
                textToSend = MessageFormatHelper.ConvertMarkdownToTelegramHtml(originalMarkdownText);
            }
            
            try
            {
                if (isEdit)
                {
                    Message editedMessage = null;
                    await sendMessageService.AddTask(async () =>
                    {
                        editedMessage = await botClient.EditMessageText(
                            chatId: chatId, messageId: messageId, parseMode: currentParseMode, text: textToSend);
                    }, isGroup);

                    if (editedMessage != null && editedMessage.MessageId > 0)
                    { logger.LogInformation($"Edited message {editedMessage.MessageId} successfully with {currentParseMode}."); }
                    else 
                    { logger.LogWarning($"Editing message {messageId} with {currentParseMode} failed silently. Edit will be skipped.");}
                }
                else 
                {
                    Message sentMsg = null;
                    await sendMessageService.AddTask(async () =>
                    {
                        sentMsg = await botClient.SendMessage(
                            chatId: chatId, text: textToSend, parseMode: currentParseMode, 
                            replyParameters: new ReplyParameters() { MessageId = replyToMessageId });
                    }, isGroup);

                    if (sentMsg != null && sentMsg.MessageId > 0)
                    { logger.LogInformation($"Sent new message {sentMsg.MessageId} successfully with {currentParseMode}."); }
                    else 
                    { 
                        logger.LogWarning($"Sending new message to {chatId} with {currentParseMode} failed silently. Attempting fallback.");
                        await AttemptFallbackSend(chatId, messageId, originalMarkdownText, isGroup, replyToMessageId, false, $"{currentParseMode} send failed silently");
                    }
                }
            }
            catch (ApiRequestException apiEx) when (apiEx.Message.Contains("can't parse entities") || apiEx.Message.Contains("unclosed tag") || apiEx.ErrorCode == 400)
            {
                if (isEdit)
                { logger.LogWarning(apiEx, $"Failed to edit message {messageId} with {currentParseMode} due to API error. Edit will be skipped."); }
                else 
                { 
                    logger.LogWarning(apiEx, $"Failed to send new message to {chatId} with {currentParseMode} due to API error. Attempting fallback.");
                    await AttemptFallbackSend(chatId, messageId, originalMarkdownText, isGroup, replyToMessageId, false, apiEx.Message);
                }
            }
            catch (Exception ex)
            {
                if (isEdit)
                { logger.LogError(ex, $"An unexpected error occurred while editing message {messageId}. Edit will be skipped.");}
                else 
                { 
                    logger.LogError(ex, $"An unexpected error occurred while sending new message to {chatId}. Attempting fallback.");
                    await AttemptFallbackSend(chatId, messageId, originalMarkdownText, isGroup, replyToMessageId, false, $"Unexpected error: {ex.Message}");
                }
            }
        }
        
        public async Task AttemptFallbackSend(long chatId, int messageId, string originalMarkdownText, bool isGroup, int replyToMessageId, bool wasEditAttempt, string initialFailureReason)
        {
            var plainText = MessageFormatHelper.ConvertToPlainText(originalMarkdownText);
            try
            {
                if (wasEditAttempt) 
                {
                    await sendMessageService.AddTask(async () => {
                        var fallbackEditedMessage = await botClient.EditMessageText(chatId: chatId, messageId: messageId, text: plainText);
                        logger.LogInformation($"Successfully resent message {fallbackEditedMessage.MessageId} as plain text after Markdown failure ({initialFailureReason}).");
                    }, isGroup);
                }
                else 
                {
                    await sendMessageService.AddTask(async () => {
                        var fallbackSentMsg = await botClient.SendMessage(chatId: chatId, text: plainText, replyParameters: new ReplyParameters() { MessageId = replyToMessageId });
                        logger.LogInformation($"Successfully sent new message {fallbackSentMsg.MessageId} as plain text after Markdown failure ({initialFailureReason}).");
                    }, isGroup);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to send message to {chatId} even as plain text. Initial failure: {initialFailureReason}.");
                if (!wasEditAttempt) { 
                    await sendMessageService.AddTask(async () => {
                        await botClient.SendMessage(chatId: chatId, text: "An error occurred while formatting the message.", replyParameters: new ReplyParameters() { MessageId = replyToMessageId });
                    }, isGroup);
                }
            }
        }
        #endregion

        #region ISendMessageService 实现
        public async Task<Message> SendTextMessageAsync(string text, long chatId, int replyToMessageId = 0, bool disableNotification = false)
        {
            return await botClient.SendMessage(
                chatId: chatId,
                text: text,
                replyParameters: replyToMessageId != 0 ? new ReplyParameters { MessageId = replyToMessageId } : null,
                disableNotification: disableNotification
            );
        }

        public async Task SplitAndSendTextMessage(string text, long chatId, int replyToMessageId = 0)
        {
            // 简化实现：直接发送完整消息
            await SendTextMessageAsync(text, chatId, replyToMessageId);
        }

        public async Task<Message> SendButtonMessageAsync(string text, long chatId, int replyToMessageId = 0, params (string text, string callbackData)[] buttons)
        {
            // 创建内联键盘
            var inlineKeyboard = buttons.Select(b => new[] { InlineKeyboardButton.WithCallbackData(b.text, b.callbackData) }).ToArray();
            var replyMarkup = new InlineKeyboardMarkup(inlineKeyboard);

            return await botClient.SendMessage(
                chatId: chatId,
                text: text,
                replyParameters: replyToMessageId != 0 ? new ReplyParameters { MessageId = replyToMessageId } : null,
                replyMarkup: replyMarkup
            );
        }

        public async Task AddTask(Func<Task> action, bool isGroup)
        {
            // 简化实现：直接执行任务
            await action();
        }

        public async Task<Message> SendPhotoAsync(long chatId, InputFile photo, string caption = null, int replyToMessageId = 0, bool disableNotification = false)
        {
            // 简化实现：直接调用BotClient发送图片
            return await botClient.SendPhoto(
                chatId: chatId,
                photo: photo,
                caption: caption,
                replyParameters: replyToMessageId != 0 ? new ReplyParameters { MessageId = replyToMessageId } : null,
                disableNotification: disableNotification
            );
        }

        public async Task Log(string text)
        {
            logger.LogInformation(text);
        }
        #endregion
    }
}

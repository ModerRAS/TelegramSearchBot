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

namespace TelegramSearchBot.Service.BotAPI
{
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public partial class SendMessageService : ISendMessageService
    {
        #region Fields and Constructor
        public string ServiceName => "SendMessageService";
        private readonly ITelegramBotClient botClient;
        private readonly SendMessage Send; 
        private readonly ILogger<SendMessageService> logger;

        public SendMessageService(ITelegramBotClient botClient, SendMessage Send, ILogger<SendMessageService> logger)
        {
            this.Send = Send;
            this.botClient = botClient;
            this.logger = logger;
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
                    await Send.AddTask(async () =>
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
                    await Send.AddTask(async () =>
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
                    await Send.AddTask(async () => {
                        var fallbackEditedMessage = await botClient.EditMessageText(chatId: chatId, messageId: messageId, text: plainText);
                        logger.LogInformation($"Successfully resent message {fallbackEditedMessage.MessageId} as plain text after Markdown failure ({initialFailureReason}).");
                    }, isGroup);
                }
                else 
                {
                    await Send.AddTask(async () => {
                        var fallbackSentMsg = await botClient.SendMessage(chatId: chatId, text: plainText, replyParameters: new ReplyParameters() { MessageId = replyToMessageId });
                        logger.LogInformation($"Successfully sent new message {fallbackSentMsg.MessageId} as plain text after Markdown failure ({initialFailureReason}).");
                    }, isGroup);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to send message to {chatId} even as plain text. Initial failure: {initialFailureReason}.");
                if (!wasEditAttempt) { 
                    await Send.AddTask(async () => {
                        await botClient.SendMessage(chatId: chatId, text: "An error occurred while formatting the message.", replyParameters: new ReplyParameters() { MessageId = replyToMessageId });
                    }, isGroup);
                }
            }
        }
        #endregion
    }
}

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
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using Markdig;
using Telegram.Bot.Exceptions;

namespace TelegramSearchBot.Service.BotAPI
{
    public class SendMessageService : IService
    {
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


        public async Task SendDocument(InputFile inputFile, long ChatId, int replyTo)
        {
            await Send.AddTask(async () =>
            {
                var message = await botClient.SendDocument(
                    chatId: ChatId,
                    document: inputFile,
                    replyParameters: new ReplyParameters() { MessageId = replyTo }
                    );
            }, ChatId < 0);
        }
        public Task SendDocument(Stream inputFile, string FileName, long ChatId, int replyTo) => SendDocument(InputFile.FromStream(inputFile, FileName), ChatId, replyTo);
        public Task SendDocument(byte[] inputFile, string FileName, long ChatId, int replyTo) => SendDocument(InputFile.FromStream(new MemoryStream(inputFile), FileName), ChatId, replyTo);
        public Task SendDocument(string inputFile, string FileName, long ChatId, int replyTo) => SendDocument(InputFile.FromStream(new MemoryStream(Encoding.UTF8.GetBytes(inputFile)), FileName), ChatId, replyTo);


        public Task SendMessage(string Text, Chat ChatId, int replyTo) => SendMessage(Text, ChatId.Id, replyTo);
        public async Task SendMessage(string Text, long ChatId, int replyTo)
        {
            await Send.AddTask(async () =>
            {
                await botClient.SendMessage(
                    chatId: ChatId,
                    disableNotification: true,
                    replyParameters: new ReplyParameters() { MessageId = replyTo },
                    text: Text
                    );
            }, ChatId < 0);
        }
        public async Task SendMessage(string Text, long ChatId)
        {
            await Send.AddTask(async () =>
            {
                await botClient.SendMessage(
                    chatId: ChatId,
                    disableNotification: true,
                    text: Text
                    );
            }, ChatId < 0);
        }
        public async IAsyncEnumerable<Model.Data.Message> SendMessage(IAsyncEnumerable<string> messages, long ChatId, int replyTo, string InitialContent = "Initializing...", ParseMode parseMode = ParseMode.Markdown)
        {
            // 初始化一条消息，准备编辑
            var sentMessage = await botClient.SendMessage(
                chatId: ChatId,
                text: InitialContent,
                replyParameters: new ReplyParameters() { MessageId = replyTo }
            );
            StringBuilder builder = new StringBuilder();
            var tmpMessageId = sentMessage.MessageId;
            var datetime = DateTime.UtcNow;
            var messagesToYield = new List<Model.Data.Message>();

            try
            {
                await foreach (var PerMessage in messages)
                {
                    if (builder.Length > 1900) // Telegram message length limit is 4096, but we leave some buffer
                    {
                        tmpMessageId = sentMessage.MessageId;
                        messagesToYield.Add(new Model.Data.Message()
                        {
                            GroupId = ChatId,
                            MessageId = sentMessage.MessageId,
                            DateTime = sentMessage.Date,
                            ReplyToUserId = (await botClient.GetMe()).Id,
                            ReplyToMessageId = tmpMessageId,
                            FromUserId = (await botClient.GetMe()).Id,
                            Content = builder.ToString(),
                        });
                        // Send current buffer and start a new message
                        await TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, builder.ToString(), parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
                        sentMessage = await botClient.SendMessage(
                            chatId: ChatId,
                            text: InitialContent, // Placeholder for the new message
                            replyParameters: new ReplyParameters() { MessageId = tmpMessageId }
                        );
                        builder.Clear();
                    }
                    builder.Append(PerMessage);
                    if (DateTime.UtcNow - datetime > TimeSpan.FromSeconds(5)) // Edit message every 5 seconds
                    {
                        datetime = DateTime.UtcNow;
                        await TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, builder.ToString(), parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
                    }
                }
                // Send the final part of the message
                await TrySendMessageWithFallback(sentMessage.Chat.Id, sentMessage.MessageId, builder.ToString(), parseMode, ChatId < 0, tmpMessageId, InitialContent, true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error sending streaming message to {ChatId}");
                // Fallback: send the remaining content as a new plain text message if something went wrong during streaming
                if (builder.Length > 0)
                {
                    try
                    {
                        await botClient.SendMessage(
                            chatId: ChatId,
                            text: "Message content could not be fully displayed due to an error. Partial content:\n" + Markdown.ToPlainText(builder.ToString()),
                            replyParameters: new ReplyParameters() { MessageId = replyTo }
                        );
                    }
                    catch (Exception fallbackEx)
                    {
                        logger.LogError(fallbackEx, $"Error sending fallback plain text message to {ChatId}");
                    }
                }
            }

            messagesToYield.Add(new Model.Data.Message()
            {
                GroupId = ChatId,
                MessageId = sentMessage.MessageId,
                DateTime = sentMessage.Date,
                ReplyToUserId = (await botClient.GetMe()).Id,
                FromUserId = (await botClient.GetMe()).Id,
                ReplyToMessageId = tmpMessageId,
                Content = builder.ToString(),
            });

            foreach (var msg in messagesToYield)
            {
                yield return msg;
            }
        }

        private async Task TrySendMessageWithFallback(long chatId, int messageId, string text, ParseMode parseMode, bool isGroup, int replyToMessageId, string initialContentForNewMessage, bool isEdit)
        {
            try
            {
                if (isEdit)
                {
                    Message editedMessage = null;
                    await Send.AddTask(async () =>
                    {
                        editedMessage = await botClient.EditMessageText(
                            chatId: chatId,
                            messageId: messageId,
                            parseMode: parseMode,
                            text: text
                        );
                    }, isGroup);

                    if (editedMessage != null && editedMessage.MessageId > 0)
                    {
                        logger.LogInformation($"Edited message {editedMessage.MessageId} successfully with {parseMode}. Content: {text}");
                    }
                    else
                    {
                        logger.LogWarning($"Editing message {messageId} with {parseMode} seems to have failed silently or returned an invalid message object. Attempting to send as plain text. Original text: {text}");
                        await AttemptPlainTextSend(chatId, messageId, text, isGroup, replyToMessageId, isEdit, "Markdown edit failed silently");
                    }
                }
                else // This case might not be directly used with the current streaming logic but good for completeness
                {
                    Message sentMsg = null;
                    await Send.AddTask(async () =>
                    {
                        sentMsg = await botClient.SendMessage(
                            chatId: chatId,
                            text: text,
                            parseMode: parseMode,
                            replyParameters: new ReplyParameters() { MessageId = replyToMessageId }
                        );
                    }, isGroup);

                    if (sentMsg != null && sentMsg.MessageId > 0)
                    {
                        logger.LogInformation($"Sent new message {sentMsg.MessageId} successfully with {parseMode}. Content: {text}");
                    }
                    else
                    {
                        logger.LogWarning($"Sending new message to {chatId} with {parseMode} seems to have failed silently or returned an invalid message object. Attempting to send as plain text. Original text: {text}");
                        await AttemptPlainTextSend(chatId, messageId, text, isGroup, replyToMessageId, isEdit, "Markdown send failed silently");
                    }
                }
            }
            catch (ApiRequestException apiEx) when (apiEx.Message.Contains("can't parse entities") || apiEx.ErrorCode == 400) // Common errors for bad Markdown
            {
                logger.LogWarning(apiEx, $"Failed to send/edit message {messageId} to {chatId} with {parseMode} due to API error: {apiEx.Message}. Attempting to send as plain text.");
                await AttemptPlainTextSend(chatId, messageId, text, isGroup, replyToMessageId, isEdit, apiEx.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"An unexpected error occurred while sending/editing message {messageId} to {chatId}. Text: {text}");
                // Handle other types of exceptions if necessary, possibly also falling back to plain text or a generic error message
            }
        }

        private async Task AttemptPlainTextSend(long chatId, int messageId, string originalText, bool isGroup, int replyToMessageId, bool wasEditAttempt, string failureReason)
        {
            var plainText = SanitizeMarkdown(originalText); // SanitizeMarkdown now converts to plain text
            try
            {
                if (wasEditAttempt)
                {
                    await Send.AddTask(async () =>
                    {
                        var fallbackEditedMessage = await botClient.EditMessageText(
                            chatId: chatId,
                            messageId: messageId, // Use original messageId for editing
                            text: plainText // Send as plain text
                        );
                        logger.LogInformation($"Successfully resent message {fallbackEditedMessage.MessageId} as plain text after Markdown failure ({failureReason}). Content: {plainText}");
                    }, isGroup);
                }
                else // Was a new message attempt
                {
                    await Send.AddTask(async () =>
                    {
                        var fallbackSentMsg = await botClient.SendMessage(
                            chatId: chatId,
                            text: plainText, // Send as plain text
                            replyParameters: new ReplyParameters() { MessageId = replyToMessageId }
                        );
                        logger.LogInformation($"Successfully sent new message {fallbackSentMsg.MessageId} as plain text after Markdown failure ({failureReason}). Content: {plainText}");
                    }, isGroup);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to send message to {chatId} (original messageId for edit: {messageId}) even as plain text. Failure reason: {failureReason}. Original text: {originalText}");
                // As a last resort, if even plain text fails (especially for new messages), send a generic error message.
                // For edits, if plain text edit fails, it might be a more fundamental issue (e.g. message deleted).
                if (!wasEditAttempt) {
                    await Send.AddTask(async () =>
                    {
                        await botClient.SendMessage(
                            chatId: chatId,
                            text: "An error occurred while formatting the message. The content could not be displayed.",
                            replyParameters: new ReplyParameters() { MessageId = replyToMessageId }
                        );
                    }, isGroup);
                }
            }
        }

        // Sanitizer: Converts potentially invalid Markdown to plain text.
        private string SanitizeMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            
            // Convert to plain text as a robust fallback for invalid Markdown structures.
            // This ensures the content can be sent, albeit without formatting.
            return Markdig.Markdown.ToPlainText(text);
        }
    }
}

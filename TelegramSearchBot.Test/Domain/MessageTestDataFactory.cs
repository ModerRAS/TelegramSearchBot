using System;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Domain.Tests
{
    /// <summary>
    /// 测试数据工厂类，用于创建标准化的测试数据
    /// </summary>
    public static class MessageTestDataFactory
    {
        /// <summary>
        /// 创建有效的 MessageOption 对象
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="chatId">聊天ID</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="content">消息内容</param>
        /// <param name="replyTo">回复的消息ID</param>
        /// <returns>MessageOption 对象</returns>
        public static MessageOption CreateValidMessageOption(
            long userId = 1L,
            long chatId = 100L,
            long messageId = 1000L,
            string content = "Test message",
            long replyTo = 0L)
        {
            return new MessageOption
            {
                UserId = userId,
                User = new User
                {
                    Id = userId,
                    FirstName = "Test",
                    LastName = "User",
                    Username = "testuser",
                    IsBot = false,
                    IsPremium = false
                },
                ChatId = chatId,
                Chat = new Chat
                {
                    Id = chatId,
                    Title = "Test Chat",
                    Type = ChatType.Group,
                    IsForum = false
                },
                MessageId = messageId,
                Content = content,
                DateTime = DateTime.UtcNow,
                ReplyTo = replyTo,
                MessageDataId = 0
            };
        }

        /// <summary>
        /// 创建有效的 Message 对象
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="fromUserId">发送者用户ID</param>
        /// <param name="content">消息内容</param>
        /// <param name="replyToMessageId">回复的消息ID</param>
        /// <returns>Message 对象</returns>
        public static Message CreateValidMessage(
            long groupId = 100L,
            long messageId = 1000L,
            long fromUserId = 1L,
            string content = "Test message",
            long replyToMessageId = 0L)
        {
            return new Message
            {
                GroupId = groupId,
                MessageId = messageId,
                FromUserId = fromUserId,
                Content = content,
                DateTime = DateTime.UtcNow,
                ReplyToUserId = replyToMessageId > 0 ? fromUserId : 0,
                ReplyToMessageId = replyToMessageId,
                MessageExtensions = new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 创建带回复的 MessageOption 对象
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="chatId">聊天ID</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="content">消息内容</param>
        /// <param name="replyToMessageId">回复的消息ID</param>
        /// <returns>MessageOption 对象</returns>
        public static MessageOption CreateMessageWithReply(
            long userId = 1L,
            long chatId = 100L,
            long messageId = 1001L,
            string content = "Reply message",
            long replyToMessageId = 1000L)
        {
            return CreateValidMessageOption(userId, chatId, messageId, content, replyToMessageId);
        }

        /// <summary>
        ///创建长消息的 MessageOption 对象
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="chatId">聊天ID</param>
        /// <param name="wordCount">单词数量</param>
        /// <returns>MessageOption 对象</returns>
        public static MessageOption CreateLongMessage(
            long userId = 1L,
            long chatId = 100L,
            int wordCount = 100)
        {
            var longContent = string.Join(" ", Enumerable.Repeat("word", wordCount));
            return CreateValidMessageOption(userId, chatId, content: longContent);
        }

        /// <summary>
        /// 创建包含特殊字符的 MessageOption 对象
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="chatId">聊天ID</param>
        /// <returns>MessageOption 对象</returns>
        public static MessageOption CreateMessageWithSpecialChars(
            long userId = 1L,
            long chatId = 100L)
        {
            var specialContent = "Message with special chars: 中文, emoji 😊, symbols @#$%, and new lines\n\t";
            return CreateValidMessageOption(userId, chatId, content: specialContent);
        }

        /// <summary>
        /// 创建用户数据对象
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="firstName">名字</param>
        /// <param name="lastName">姓氏</param>
        /// <param name="username">用户名</param>
        /// <returns>UserData 对象</returns>
        public static UserData CreateUserData(
            long userId = 1L,
            string firstName = "Test",
            string lastName = "User",
            string username = "testuser")
        {
            return new UserData
            {
                Id = userId,
                FirstName = firstName,
                LastName = lastName,
                UserName = username,
                IsBot = false,
                IsPremium = false
            };
        }

        /// <summary>
        /// 创建群组数据对象
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="title">群组标题</param>
        /// <param name="type">群组类型</param>
        /// <returns>GroupData 对象</returns>
        public static GroupData CreateGroupData(
            long groupId = 100L,
            string title = "Test Chat",
            string type = "Group")
        {
            return new GroupData
            {
                Id = groupId,
                Title = title,
                Type = type,
                IsForum = false
            };
        }

        /// <summary>
        /// 创建用户群组关联对象
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="groupId">群组ID</param>
        /// <returns>UserWithGroup 对象</returns>
        public static UserWithGroup CreateUserWithGroup(
            long userId = 1L,
            long groupId = 100L)
        {
            return new UserWithGroup
            {
                UserId = userId,
                GroupId = groupId
            };
        }

        /// <summary>
        /// 创建消息扩展对象
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="extensionType">扩展类型</param>
        /// <param name="extensionData">扩展数据</param>
        /// <returns>MessageExtension 对象</returns>
        public static MessageExtension CreateMessageExtension(
            long messageId = 1L,
            string extensionType = "OCR",
            string extensionData = "Extracted text from image")
        {
            return new MessageExtension
            {
                MessageId = messageId,
                ExtensionType = extensionType,
                ExtensionData = extensionData
            };
        }
    }

    /// <summary>
    /// 测试数据构建器，提供链式调用来创建复杂的测试数据
    /// </summary>
    public class MessageOptionBuilder
    {
        private MessageOption _messageOption = new MessageOption();

        public MessageOptionBuilder WithUserId(long userId)
        {
            _messageOption.UserId = userId;
            _messageOption.User = new User { Id = userId };
            return this;
        }

        public MessageOptionBuilder WithChatId(long chatId)
        {
            _messageOption.ChatId = chatId;
            _messageOption.Chat = new Chat { Id = chatId };
            return this;
        }

        public MessageOptionBuilder WithMessageId(long messageId)
        {
            _messageOption.MessageId = messageId;
            return this;
        }

        public MessageOptionBuilder WithContent(string content)
        {
            _messageOption.Content = content;
            return this;
        }

        public MessageOptionBuilder WithReplyTo(long replyTo)
        {
            _messageOption.ReplyTo = replyTo;
            return this;
        }

        public MessageOptionBuilder WithUser(User user)
        {
            _messageOption.User = user;
            _messageOption.UserId = user.Id;
            return this;
        }

        public MessageOptionBuilder WithChat(Chat chat)
        {
            _messageOption.Chat = chat;
            _messageOption.ChatId = chat.Id;
            return this;
        }

        public MessageOptionBuilder WithDateTime(DateTime dateTime)
        {
            _messageOption.DateTime = dateTime;
            return this;
        }

        public MessageOptionBuilder WithMessageDataId(long messageDataId)
        {
            _messageOption.MessageDataId = messageDataId;
            return this;
        }

        public MessageOption Build()
        {
            // 确保必需的属性有默认值
            if (_messageOption.User == null)
            {
                _messageOption.User = new User { Id = _messageOption.UserId };
            }
            if (_messageOption.Chat == null)
            {
                _messageOption.Chat = new Chat { Id = _messageOption.ChatId };
            }
            if (_messageOption.DateTime == default)
            {
                _messageOption.DateTime = DateTime.UtcNow;
            }
            
            return _messageOption;
        }
    }

    /// <summary>
    /// Message 对象构建器
    /// </summary>
    public class MessageBuilder
    {
        private Message _message = new Message();

        public MessageBuilder WithGroupId(long groupId)
        {
            _message.GroupId = groupId;
            return this;
        }

        public MessageBuilder WithMessageId(long messageId)
        {
            _message.MessageId = messageId;
            return this;
        }

        public MessageBuilder WithFromUserId(long fromUserId)
        {
            _message.FromUserId = fromUserId;
            return this;
        }

        public MessageBuilder WithContent(string content)
        {
            _message.Content = content;
            return this;
        }

        public MessageBuilder WithDateTime(DateTime dateTime)
        {
            _message.DateTime = dateTime;
            return this;
        }

        public MessageBuilder WithReplyTo(long replyToMessageId, long replyToUserId = 0)
        {
            _message.ReplyToMessageId = replyToMessageId;
            _message.ReplyToUserId = replyToUserId;
            return this;
        }

        public MessageBuilder WithExtensions(List<MessageExtension> extensions)
        {
            _message.MessageExtensions = extensions;
            return this;
        }

        public Message Build()
        {
            if (_message.DateTime == default)
            {
                _message.DateTime = DateTime.UtcNow;
            }
            if (_message.MessageExtensions == null)
            {
                _message.MessageExtensions = new List<MessageExtension>();
            }
            
            return _message;
        }
    }
}
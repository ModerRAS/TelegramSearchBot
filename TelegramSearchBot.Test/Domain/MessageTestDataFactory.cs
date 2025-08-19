using System;
using System.Linq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using Message = TelegramSearchBot.Model.Data.Message;

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
        public static TelegramSearchBot.Model.MessageOption CreateValidMessageOption(
            long userId = 1L,
            long chatId = 100L,
            long messageId = 1000L,
            string content = "Test message",
            long replyTo = 0L)
        {
            return new TelegramSearchBot.Model.MessageOption
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
                ReplyTo = replyTo
            };
        }

        /// <summary>
        /// 创建有效的 Message 对象
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="userId">用户ID</param>
        /// <param name="content">消息内容</param>
        /// <param name="replyToUserId">回复的用户ID</param>
        /// <param name="replyToMessageId">回复的消息ID</param>
        /// <returns>Message 对象</returns>
        public static TelegramSearchBot.Model.Data.Message CreateValidMessage(
            long groupId = 100L,
            long messageId = 1000L,
            long userId = 1L,
            string content = "Test message",
            long replyToUserId = 0L,
            long replyToMessageId = 0L)
        {
            return new TelegramSearchBot.Model.Data.Message
            {
                GroupId = groupId,
                MessageId = messageId,
                FromUserId = userId,
                ReplyToUserId = replyToUserId,
                ReplyToMessageId = replyToMessageId,
                Content = content,
                DateTime = DateTime.UtcNow,
                MessageExtensions = new List<MessageExtension>()
            };
        }

        /// <summary>
        /// 创建有效的 Message 对象（支持自定义时间）
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="content">消息内容</param>
        /// <param name="dateTime">消息时间</param>
        /// <param name="userId">用户ID</param>
        /// <param name="replyToUserId">回复的用户ID</param>
        /// <param name="replyToMessageId">回复的消息ID</param>
        /// <returns>Message 对象</returns>
        public static TelegramSearchBot.Model.Data.Message CreateValidMessage(
            long groupId,
            long messageId,
            string content,
            DateTime dateTime,
            long userId = 1L,
            long replyToUserId = 0L,
            long replyToMessageId = 0L)
        {
            return new TelegramSearchBot.Model.Data.Message
            {
                GroupId = groupId,
                MessageId = messageId,
                FromUserId = userId,
                ReplyToUserId = replyToUserId,
                ReplyToMessageId = replyToMessageId,
                Content = content,
                DateTime = dateTime,
                MessageExtensions = new List<MessageExtension>()
            };
        }

        /// <summary>
        ///创建有效的 UserData 对象
        /// </summary>
        /// <param name="id">用户ID</param>
        /// <param name="firstName">名字</param>
        /// <param name="lastName">姓氏</param>
        /// <param name="username">用户名</param>
        /// <param name="isBot">是否为机器人</param>
        /// <returns>UserData 对象</returns>
        public static UserData CreateUserData(
            long id = 1L,
            string firstName = "Test",
            string lastName = "User",
            string username = "testuser",
            bool isBot = false)
        {
            return new UserData
            {
                Id = id,
                FirstName = firstName,
                LastName = lastName,
                UserName = username,
                IsBot = isBot,
                IsPremium = false
            };
        }

        /// <summary>
        /// 创建有效的 GroupData 对象
        /// </summary>
        /// <param name="id">群组ID</param>
        /// <param name="title">群组标题</param>
        /// <param name="type">群组类型</param>
        /// <returns>GroupData 对象</returns>
        public static GroupData CreateGroupData(
            long id = 100L,
            string title = "Test Chat",
            string type = "Group")
        {
            return new GroupData
            {
                Id = id,
                Title = title,
                Type = type,
                IsForum = false,
                IsBlacklist = false
            };
        }

        /// <summary>
        /// 创建有效的 UserWithGroup 对象
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="groupId">群组ID</param>
        /// <param name="status">状态</param>
        /// <returns>UserWithGroup 对象</returns>
        public static UserWithGroup CreateUserWithGroup(
            long userId = 1L,
            long groupId = 100L,
            string status = "member")
        {
            return new UserWithGroup
            {
                UserId = userId,
                GroupId = groupId
            };
        }

        /// <summary>
        /// 创建有效的 MessageExtension 对象
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="type">扩展类型</param>
        /// <param name="value">扩展值</param>
        /// <returns>MessageExtension 对象</returns>
        public static MessageExtension CreateMessageExtension(
            long messageId = 1L,
            string type = "test",
            string value = "test value")
        {
            return new TelegramSearchBot.Model.Data.MessageExtension
            {
                // 简化实现：MessageExtension属性名可能已经更改
                // 原本实现：使用MessageId, Type, Value, CreatedAt属性
                // 简化实现：根据当前MessageExtension类的实际属性进行调整
                MessageDataId = messageId,
                ExtensionType = type,
                ExtensionData = value
            };
        }

        /// <summary>
        /// 创建包含特殊字符的测试消息
        /// </summary>
        /// <param name="includeChinese">是否包含中文</param>
        /// <param name="includeEmoji">是否包含表情符号</param>
        /// <param name="includeSpecialChars">是否包含特殊字符</param>
        /// <returns>包含特殊字符的MessageOption</returns>
        public static TelegramSearchBot.Model.MessageOption CreateMessageWithSpecialCharacters(
            bool includeChinese = true,
            bool includeEmoji = true,
            bool includeSpecialChars = true)
        {
            var content = "Test message";
            
            if (includeChinese)
            {
                content += " 中文测试";
            }
            
            if (includeEmoji)
            {
                content += " 😊🎉";
            }
            
            if (includeSpecialChars)
            {
                content += " @#$%^&*()";
            }
            
            return CreateValidMessageOption(content: content);
        }

        /// <summary>
        /// 创建包含特殊字符的测试消息（简化方法名）
        /// </summary>
        /// <returns>包含特殊字符的MessageOption</returns>
        public static TelegramSearchBot.Model.MessageOption CreateMessageWithSpecialChars()
        {
            return CreateMessageWithSpecialCharacters(true, true, true);
        }

        /// <summary>
        /// 创建长消息（超过4000字符）
        /// </summary>
        /// <param name="targetLength">目标长度</param>
        /// <returns>长消息的MessageOption</returns>
        public static TelegramSearchBot.Model.MessageOption CreateLongMessage(int targetLength = 5000)
        {
            var content = new string('a', targetLength);
            return CreateValidMessageOption(content: content);
        }

        /// <summary>
        /// 创建长消息（按单词数量）
        /// </summary>
        /// <param name="wordCount">单词数量</param>
        /// <returns>长消息的MessageOption</returns>
        public static TelegramSearchBot.Model.MessageOption CreateLongMessageByWords(int wordCount = 100)
        {
            var words = Enumerable.Repeat("word", wordCount);
            var content = string.Join(" ", words) + $" Long message with {wordCount} words";
            return CreateValidMessageOption(content: content);
        }

        /// <summary>
        /// 创建带有回复消息的MessageOption
        /// </summary>
        /// <param name="replyToMessageId">回复的消息ID</param>
        /// <param name="replyToUserId">回复的用户ID</param>
        /// <returns>带有回复的MessageOption</returns>
        public static TelegramSearchBot.Model.MessageOption CreateMessageWithReply(
            long replyToMessageId = 1000L,
            long replyToUserId = 1L)
        {
            return CreateValidMessageOption(
                content: "This is a reply message",
                replyTo: replyToMessageId);
        }

        /// <summary>
        /// 创建带有回复消息的MessageOption（重载方法）
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="chatId">聊天ID</param>
        /// <param name="messageId">消息ID</param>
        /// <param name="content">消息内容</param>
        /// <param name="replyToMessageId">回复的消息ID</param>
        /// <returns>带有回复的MessageOption</returns>
        public static TelegramSearchBot.Model.MessageOption CreateMessageWithReply(
            long userId,
            long chatId,
            long messageId,
            string content,
            long replyToMessageId)
        {
            return CreateValidMessageOption(
                userId: userId,
                chatId: chatId,
                messageId: messageId,
                content: content,
                replyTo: replyToMessageId);
        }

        /// <summary>
        /// 创建标准的测试数据集
        /// </summary>
        /// <returns>包含标准测试数据的元组</returns>
        public static (TelegramSearchBot.Model.Data.Message Message, UserData User, GroupData Group, UserWithGroup UserWithGroup) CreateStandardTestData()
        {
            var group = CreateGroupData();
            var user = CreateUserData();
            var userWithGroup = CreateUserWithGroup(user.Id, group.Id);
            var message = CreateValidMessage(group.Id, 1000, user.Id, "Standard test message");
            
            return (message, user, group, userWithGroup);
        }

        /// <summary>
        /// 创建批量的测试消息
        /// </summary>
        /// <param name="count">消息数量</param>
        /// <param name="groupId">群组ID</param>
        /// <param name="startMessageId">起始消息ID</param>
        /// <returns>批量消息列表</returns>
        public static List<TelegramSearchBot.Model.MessageOption> CreateBatchMessageOptions(
            int count = 10,
            long groupId = 100L,
            long startMessageId = 1000L)
        {
            var messages = new List<TelegramSearchBot.Model.MessageOption>();
            
            for (int i = 0; i < count; i++)
            {
                messages.Add(CreateValidMessageOption(
                    userId: (i % 3) + 1, // 轮换用户
                    chatId: groupId,
                    messageId: startMessageId + i,
                    content: $"Batch message {i + 1}"
                ));
            }
            
            return messages;
        }

        /// <summary>
        /// 创建用于搜索测试的多样化消息
        /// </summary>
        /// <param name="groupId">群组ID</param>
        /// <returns>多样化消息列表</returns>
        public static List<TelegramSearchBot.Model.MessageOption> CreateSearchTestMessages(long groupId = 100L)
        {
            return new List<TelegramSearchBot.Model.MessageOption>
            {
                CreateValidMessageOption(userId: 1, chatId: groupId, messageId: 1001, content: "Hello world"),
                CreateValidMessageOption(userId: 2, chatId: groupId, messageId: 1002, content: "Search functionality test"),
                CreateValidMessageOption(userId: 1, chatId: groupId, messageId: 1003, content: "Database query optimization"),
                CreateValidMessageOption(userId: 3, chatId: groupId, messageId: 1004, content: "中文搜索测试"),
                CreateValidMessageOption(userId: 2, chatId: groupId, messageId: 1005, content: "Emoji test 😊"),
                CreateValidMessageOption(userId: 1, chatId: groupId, messageId: 1006, content: "Special characters @#$%"),
                CreateValidMessageOption(userId: 3, chatId: groupId, messageId: 1007, content: "Long message content " + new string('a', 100)),
                CreateValidMessageOption(userId: 2, chatId: groupId, messageId: 1008, content: "Reply message", replyTo: 1001),
                CreateValidMessageOption(userId: 1, chatId: groupId, messageId: 1009, content: "Empty content test"),
                CreateValidMessageOption(userId: 3, chatId: groupId, messageId: 1010, content: "Final search test message")
            };
        }
    }
}
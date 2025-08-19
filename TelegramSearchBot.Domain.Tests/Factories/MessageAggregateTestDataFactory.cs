using System;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Domain.Message;

namespace TelegramSearchBot.Domain.Tests.Factories
{
    /// <summary>
    /// 消息聚合测试数据工厂
    /// </summary>
    public static class MessageAggregateTestDataFactory
    {
        /// <summary>
        /// 创建标准的消息聚合
        /// </summary>
        public static MessageAggregate CreateStandardMessage()
        {
            return MessageAggregate.Create(
                chatId: 123456789,
                messageId: 1,
                content: "这是一条测试消息",
                fromUserId: 987654321,
                timestamp: DateTime.Now
            );
        }

        /// <summary>
        /// 创建带回复的消息聚合
        /// </summary>
        public static MessageAggregate CreateMessageWithReply()
        {
            return MessageAggregate.Create(
                chatId: 123456789,
                messageId: 2,
                content: "这是一条回复消息",
                fromUserId: 987654321,
                replyToUserId: 111222333,
                replyToMessageId: 1,
                timestamp: DateTime.Now
            );
        }

        /// <summary>
        /// 创建长文本消息聚合
        /// </summary>
        public static MessageAggregate CreateLongMessage()
        {
            return MessageAggregate.Create(
                chatId: 123456789,
                messageId: 3,
                content: new string('A', 5000), // 5000字符的长文本
                fromUserId: 987654321,
                timestamp: DateTime.Now
            );
        }

        /// <summary>
        /// 创建包含特殊字符的消息聚合
        /// </summary>
        public static MessageAggregate CreateMessageWithSpecialChars()
        {
            return MessageAggregate.Create(
                chatId: 123456789,
                messageId: 4,
                content: "消息包含特殊字符：@#$%^&*()_+-=[]{}|;':\",./<>?",
                fromUserId: 987654321,
                timestamp: DateTime.Now
            );
        }

        /// <summary>
        /// 创建旧消息聚合（用于测试时间相关逻辑）
        /// </summary>
        public static MessageAggregate CreateOldMessage()
        {
            return MessageAggregate.Create(
                chatId: 123456789,
                messageId: 5,
                content: "这是一条旧消息",
                fromUserId: 987654321,
                timestamp: DateTime.Now.AddDays(-30)
            );
        }

        /// <summary>
        /// 创建空消息聚合（用于测试边界条件）
        /// </summary>
        public static MessageAggregate CreateEmptyMessage()
        {
            return MessageAggregate.Create(
                chatId: 123456789,
                messageId: 6,
                content: "",
                fromUserId: 987654321,
                timestamp: DateTime.Now
            );
        }

        /// <summary>
        /// 创建多个消息聚合（用于测试批量操作）
        /// </summary>
        public static MessageAggregate[] CreateMultipleMessages(int count = 10)
        {
            var messages = new MessageAggregate[count];
            for (int i = 0; i < count; i++)
            {
                messages[i] = MessageAggregate.Create(
                    chatId: 123456789,
                    messageId: i + 1,
                    content: $"批量消息 {i + 1}",
                    fromUserId: 987654321,
                    timestamp: DateTime.Now.AddMinutes(-i)
                );
            }
            return messages;
        }
    }
}
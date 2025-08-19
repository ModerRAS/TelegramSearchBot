using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace TelegramSearchBot.Search.Tests.Extensions
{
    /// <summary>
    /// 搜索测试的简化断言扩展类
    /// </summary>
    public static class SearchTestAssertionExtensions
    {
        /// <summary>
        /// 验证Message对象的基本属性
        /// </summary>
        /// <param name="message">消息对象</param>
        /// <param name="expectedGroupId">期望的群组ID</param>
        /// <param name="expectedMessageId">期望的消息ID</param>
        /// <param name="expectedUserId">期望的用户ID</param>
        /// <param name="expectedContent">期望的消息内容</param>
        public static void ShouldBeValidMessage(this Message message, 
            long expectedGroupId, 
            long expectedMessageId, 
            long expectedUserId, 
            string expectedContent)
        {
            Assert.NotNull(message);
            Assert.Equal(expectedGroupId, message.GroupId);
            Assert.Equal(expectedMessageId, message.MessageId);
            Assert.Equal(expectedUserId, message.FromUserId);
            Assert.Equal(expectedContent, message.Content);
            Assert.NotEqual(default, message.DateTime);
        }

        /// <summary>
        /// 验证消息集合包含指定内容
        /// </summary>
        /// <param name="messages">消息集合</param>
        /// <param name="expectedContent">期望的内容</param>
        public static void ShouldContainMessageWithContent(this IEnumerable<Message> messages, string expectedContent)
        {
            Assert.NotNull(messages);
            var message = messages.FirstOrDefault(m => m.Content.Contains(expectedContent));
            Assert.NotNull(message);
            Assert.Contains(expectedContent, message.Content);
        }

        /// <summary>
        /// 验证消息集合不包含指定内容
        /// </summary>
        /// <param name="messages">消息集合</param>
        /// <param name="forbiddenContent">禁止的内容</param>
        public static void ShouldNotContainMessageWithContent(this IEnumerable<Message> messages, string forbiddenContent)
        {
            Assert.NotNull(messages);
            Assert.DoesNotContain(messages, m => m.Content.Contains(forbiddenContent));
        }

        /// <summary>
        /// 验证消息集合按时间排序
        /// </summary>
        /// <param name="messages">消息集合</param>
        public static void ShouldBeInChronologicalOrder(this IEnumerable<Message> messages)
        {
            Assert.NotNull(messages);
            var messageList = messages.ToList();
            Assert.NotEmpty(messageList);
            
            for (int i = 1; i < messageList.Count; i++)
            {
                Assert.True(messageList[i - 1].DateTime <= messageList[i].DateTime,
                    $"Messages are not in chronological order. Message at index {i - 1} ({messageList[i - 1].DateTime}) is after message at index {i} ({messageList[i].DateTime})");
            }
        }

        /// <summary>
        /// 异步验证任务应在指定时间内完成
        /// </summary>
        /// <param name="task">要验证的任务</param>
        /// <param name="timeout">超时时间</param>
        /// <returns>异步任务</returns>
        public static async Task ShouldCompleteWithinAsync(this Task task, TimeSpan timeout)
        {
            var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
            
            if (completedTask != task)
            {
                throw new XunitException($"Task did not complete within {timeout.TotalMilliseconds}ms");
            }
            
            await task; // 重新抛出任何异常
        }

        /// <summary>
        /// 验证日期时间是最近的（指定时间范围内）
        /// </summary>
        /// <param name="dateTime">日期时间</param>
        /// <param name="maxAge">最大年龄</param>
        public static void ShouldBeRecent(this DateTime dateTime, TimeSpan maxAge)
        {
            var now = DateTime.UtcNow;
            var age = now - dateTime;
            Assert.True(age <= maxAge, $"DateTime {dateTime} is not recent. Age: {age.TotalMilliseconds}ms, Max allowed: {maxAge.TotalMilliseconds}ms");
        }
    }
}
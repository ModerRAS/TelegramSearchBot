using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace TelegramSearchBot.Test.Extensions
{
    /// <summary>
    /// 自定义断言扩展类，提供领域对象特定的断言方法
    /// </summary>
    public static class TestAssertionExtensions
    {
        #region Message Assertions

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
        /// 验证Message对象是回复消息
        /// </summary>
        /// <param name="message">消息对象</param>
        /// <param name="expectedReplyToMessageId">期望的回复消息ID</param>
        /// <param name="expectedReplyToUserId">期望的回复用户ID</param>
        public static void ShouldBeReplyMessage(this Message message, 
            long expectedReplyToMessageId, 
            long expectedReplyToUserId)
        {
            Assert.NotNull(message);
            Assert.NotEqual(0, message.ReplyToMessageId);
            Assert.NotEqual(0, message.ReplyToUserId);
            Assert.Equal(expectedReplyToMessageId, message.ReplyToMessageId);
            Assert.Equal(expectedReplyToUserId, message.ReplyToUserId);
        }

        /// <summary>
        /// 验证Message对象包含扩展数据
        /// </summary>
        /// <param name="message">消息对象</param>
        /// <param name="expectedExtensionCount">期望的扩展数量</param>
        public static void ShouldHaveExtensions(this Message message, int expectedExtensionCount)
        {
            Assert.NotNull(message);
            Assert.NotNull(message.MessageExtensions);
            Assert.Equal(expectedExtensionCount, message.MessageExtensions.Count);
        }

        /// <summary>
        /// 验证Message对象包含指定类型的扩展
        /// </summary>
        /// <param name="message">消息对象</param>
        /// <param name="extensionType">扩展类型</param>
        /// <param name="expectedData">期望的扩展数据</param>
        public static void ShouldHaveExtension(this Message message, string extensionType, string expectedData)
        {
            Assert.NotNull(message);
            Assert.NotNull(message.MessageExtensions);
            
            var extension = message.MessageExtensions.FirstOrDefault(e => e.ExtensionType == extensionType);
            Assert.NotNull(extension);
            Assert.Equal(expectedData, extension.ExtensionData);
        }

        #endregion

        #region MessageOption Assertions

        /// <summary>
        /// 验证MessageOption对象的基本属性
        /// </summary>
        /// <param name="messageOption">消息选项对象</param>
        /// <param name="expectedUserId">期望的用户ID</param>
        /// <param name="expectedChatId">期望的聊天ID</param>
        /// <param name="expectedMessageId">期望的消息ID</param>
        /// <param name="expectedContent">期望的消息内容</param>
        public static void ShouldBeValidMessageOption(this MessageOption messageOption,
            long expectedUserId,
            long expectedChatId,
            long expectedMessageId,
            string expectedContent)
        {
            Assert.NotNull(messageOption);
            Assert.Equal(expectedUserId, messageOption.UserId);
            Assert.Equal(expectedChatId, messageOption.ChatId);
            Assert.Equal(expectedMessageId, messageOption.MessageId);
            Assert.Equal(expectedContent, messageOption.Content);
            Assert.NotNull(messageOption.User);
            Assert.NotNull(messageOption.Chat);
            Assert.NotEqual(default, messageOption.DateTime);
        }

        /// <summary>
        /// 验证MessageOption对象是回复消息
        /// </summary>
        /// <param name="messageOption">消息选项对象</param>
        /// <param name="expectedReplyTo">期望的回复消息ID</param>
        public static void ShouldBeReplyMessageOption(this MessageOption messageOption, long expectedReplyTo)
        {
            Assert.NotNull(messageOption);
            Assert.NotEqual(0, messageOption.ReplyTo);
            Assert.Equal(expectedReplyTo, messageOption.ReplyTo);
        }

        #endregion

        #region User Data Assertions

        /// <summary>
        /// 验证UserData对象的基本属性
        /// </summary>
        /// <param name="userData">用户数据对象</param>
        /// <param name="expectedFirstName">期望的名字</param>
        /// <param name="expectedLastName">期望的姓氏</param>
        /// <param name="expectedUsername">期望的用户名</param>
        /// <param name="expectedIsBot">期望的是否为机器人</param>
        public static void ShouldBeValidUserData(this UserData userData,
            string expectedFirstName,
            string expectedLastName,
            string expectedUsername,
            bool expectedIsBot)
        {
            Assert.NotNull(userData);
            Assert.Equal(expectedFirstName, userData.FirstName);
            Assert.Equal(expectedLastName, userData.LastName);
            Assert.Equal(expectedUsername, userData.UserName);
            Assert.Equal(expectedIsBot, userData.IsBot);
            Assert.NotEqual(0, userData.Id);
        }

        /// <summary>
        /// 验证UserData对象是高级用户
        /// </summary>
        /// <param name="userData">用户数据对象</param>
        public static void ShouldBePremiumUser(this UserData userData)
        {
            Assert.NotNull(userData);
            Assert.True(userData.IsPremium);
        }

        /// <summary>
        /// 验证UserData对象是机器人
        /// </summary>
        /// <param name="userData">用户数据对象</param>
        public static void ShouldBeBotUser(this UserData userData)
        {
            Assert.NotNull(userData);
            Assert.True(userData.IsBot);
        }

        #endregion

        #region Group Data Assertions

        /// <summary>
        /// 验证GroupData对象的基本属性
        /// </summary>
        /// <param name="groupData">群组数据对象</param>
        /// <param name="expectedTitle">期望的标题</param>
        /// <param name="expectedType">期望的类型</param>
        /// <param name="expectedIsForum">期望的是否为论坛</param>
        public static void ShouldBeValidGroupData(this GroupData groupData,
            string expectedTitle,
            string expectedType,
            bool expectedIsForum)
        {
            Assert.NotNull(groupData);
            Assert.Equal(expectedTitle, groupData.Title);
            Assert.Equal(expectedType, groupData.Type);
            Assert.Equal(expectedIsForum, groupData.IsForum);
            Assert.NotEqual(0, groupData.Id);
        }

        /// <summary>
        /// 验证GroupData对象是论坛
        /// </summary>
        /// <param name="groupData">群组数据对象</param>
        public static void ShouldBeForum(this GroupData groupData)
        {
            Assert.NotNull(groupData);
            Assert.True(groupData.IsForum);
        }

        /// <summary>
        /// 验证GroupData对象在黑名单中
        /// </summary>
        /// <param name="groupData">群组数据对象</param>
        public static void ShouldBeBlacklisted(this GroupData groupData)
        {
            Assert.NotNull(groupData);
            Assert.True(groupData.IsBlacklist);
        }

        #endregion

        #region LLM Channel Assertions

        /// <summary>
        /// 验证LLMChannel对象的基本属性
        /// </summary>
        /// <param name="llmChannel">LLM通道对象</param>
        /// <param name="expectedName">期望的名称</param>
        /// <param name="expectedProvider">期望的提供商</param>
        /// <param name="expectedGateway">期望的网关</param>
        public static void ShouldBeValidLLMChannel(this LLMChannel llmChannel,
            string expectedName,
            LLMProvider expectedProvider,
            string expectedGateway)
        {
            Assert.NotNull(llmChannel);
            Assert.Equal(expectedName, llmChannel.Name);
            Assert.Equal(expectedProvider, llmChannel.Provider);
            Assert.Equal(expectedGateway, llmChannel.Gateway);
            Assert.NotEqual(0, llmChannel.Id);
        }

        /// <summary>
        /// 验证LLMChannel对象可用
        /// </summary>
        /// <param name="llmChannel">LLM通道对象</param>
        public static void ShouldBeAvailable(this LLMChannel llmChannel)
        {
            Assert.NotNull(llmChannel);
            Assert.True(llmChannel.IsEnabled);
        }

        /// <summary>
        /// 验证LLMChannel对象有API密钥
        /// </summary>
        /// <param name="llmChannel">LLM通道对象</param>
        public static void ShouldHaveApiKey(this LLMChannel llmChannel)
        {
            Assert.NotNull(llmChannel);
            Assert.False(string.IsNullOrEmpty(llmChannel.ApiKey));
        }

        #endregion

        #region Collection Assertions

        /// <summary>
        /// 验证消息集合不为空且按时间排序
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
        /// 验证消息集合包含指定内容
        /// </summary>
        /// <param name="messages">消息集合</param>
        /// <param name="expectedContent">期望的内容</param>
        public static void ShouldContainMessageWithContent(this IEnumerable<Message> messages, string expectedContent)
        {
            Assert.NotNull(messages);
            var message = messages.FirstOrDefault(m => m.Content.Contains(expectedContent));
            Assert.NotNull(message, $"No message found containing content: {expectedContent}");
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
        /// 验证用户集合包含指定用户名
        /// </summary>
        /// <param name="users">用户集合</param>
        /// <param name="expectedUsername">期望的用户名</param>
        public static void ShouldContainUserWithUsername(this IEnumerable<UserData> users, string expectedUsername)
        {
            Assert.NotNull(users);
            var user = users.FirstOrDefault(u => u.UserName == expectedUsername);
            Assert.NotNull(user, $"No user found with username: {expectedUsername}");
        }

        /// <summary>
        /// 验证群组集合包含指定标题
        /// </summary>
        /// <param name="groups">群组集合</param>
        /// <param name="expectedTitle">期望的标题</param>
        public static void ShouldContainGroupWithTitle(this IEnumerable<GroupData> groups, string expectedTitle)
        {
            Assert.NotNull(groups);
            var group = groups.FirstOrDefault(g => g.Title == expectedTitle);
            Assert.NotNull(group, $"No group found with title: {expectedTitle}");
        }

        #endregion

        #region Async Assertions

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
        /// 异步验证任务应抛出指定类型的异常
        /// </summary>
        /// <typeparam name="T">异常类型</typeparam>
        /// <param name="task">要验证的任务</param>
        /// <returns>异步任务</returns>
        public static async Task ShouldThrowAsync<T>(this Task task) where T : Exception
        {
            var exception = await Assert.ThrowsAsync<T>(() => task);
            Assert.NotNull(exception);
        }

        /// <summary>
        /// 异步验证任务应抛出异常（不指定类型）
        /// </summary>
        /// <param name="task">要验证的任务</param>
        /// <returns>异步任务</returns>
        public static async Task ShouldThrowAsync(this Task task)
        {
            await Assert.ThrowsAsync<Exception>(() => task);
        }

        #endregion

        #region String Assertions

        /// <summary>
        /// 验证字符串包含中文
        /// </summary>
        /// <param name="text">文本</param>
        public static void ShouldContainChinese(this string text)
        {
            Assert.NotNull(text);
            Assert.Matches(@"[\u4e00-\u9fff]", text);
        }

        /// <summary>
        /// 验证字符串包含表情符号
        /// </summary>
        /// <param name="text">文本</param>
        public static void ShouldContainEmoji(this string text)
        {
            Assert.NotNull(text);
            Assert.Matches(@"[\p{So}]", text);
        }

        /// <summary>
        /// 验证字符串包含特殊字符
        /// </summary>
        /// <param name="text">文本</param>
        public static void ShouldContainSpecialCharacters(this string text)
        {
            Assert.NotNull(text);
            Assert.Matches(@"[^\w\s]", text);
        }

        /// <summary>
        /// 验证字符串长度在指定范围内
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="minLength">最小长度</param>
        /// <param name="maxLength">最大长度</param>
        public static void ShouldHaveLengthBetween(this string text, int minLength, int maxLength)
        {
            Assert.NotNull(text);
            Assert.InRange(text.Length, minLength, maxLength);
        }

        #endregion

        #region DateTime Assertions

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

        /// <summary>
        /// 验证日期时间在指定范围内
        /// </summary>
        /// <param name="dateTime">日期时间</param>
        /// <param name="minDateTime">最小日期时间</param>
        /// <param name="maxDateTime">最大日期时间</param>
        public static void ShouldBeBetween(this DateTime dateTime, DateTime minDateTime, DateTime maxDateTime)
        {
            Assert.True(dateTime >= minDateTime && dateTime <= maxDateTime,
                $"DateTime {dateTime} is not between {minDateTime} and {maxDateTime}");
        }

        #endregion

        #region Numeric Assertions

        /// <summary>
        /// 验证数值是正数
        /// </summary>
        /// <param name="value">数值</param>
        public static void ShouldBePositive(this long value)
        {
            Assert.True(value > 0, $"Expected positive number, but got {value}");
        }

        /// <summary>
        /// 验证数值是负数
        /// </summary>
        /// <param name="value">数值</param>
        public static void ShouldBeNegative(this long value)
        {
            Assert.True(value < 0, $"Expected negative number, but got {value}");
        }

        /// <summary>
        /// 验证数值在指定范围内
        /// </summary>
        /// <param name="value">数值</param>
        /// <param name="minValue">最小值</param>
        /// <param name="maxValue">最大值</param>
        public static void ShouldBeBetween(this long value, long minValue, long maxValue)
        {
            Assert.InRange(value, minValue, maxValue);
        }

        #endregion

        #region Exception Assertions

        /// <summary>
        /// 验证异常包含指定消息
        /// </summary>
        /// <param name="exception">异常</param>
        /// <param name="expectedMessage">期望的消息</param>
        public static void ShouldContainMessage(this Exception exception, string expectedMessage)
        {
            Assert.NotNull(exception);
            Assert.Contains(expectedMessage, exception.Message);
        }

        /// <summary>
        /// 验证异常是指定类型
        /// </summary>
        /// <typeparam name="T">异常类型</typeparam>
        /// <param name="exception">异常</param>
        public static void ShouldBeOfType<T>(this Exception exception) where T : Exception
        {
            Assert.NotNull(exception);
            Assert.IsType<T>(exception);
        }

        #endregion
    }
}
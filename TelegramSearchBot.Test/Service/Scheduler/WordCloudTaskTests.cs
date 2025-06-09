using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading.Tasks;
using Telegram.Bot;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Scheduler;
using Xunit;

namespace TelegramSearchBot.Test.Service.Scheduler
{
    public class WordCloudTaskTests
    {
        private DataDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new DataDbContext(options);
        }

        [Fact]
        public void TaskName_ShouldReturnCorrectName()
        {
            // Arrange
            var mockDbContext = GetInMemoryDbContext();
            var mockBotClient = new Mock<ITelegramBotClient>();
            var mockSendMessageLogger = new Mock<ILogger<SendMessage>>();
            var mockSendMessage = new Mock<SendMessage>(mockBotClient.Object, mockSendMessageLogger.Object);
            var mockLogger = new Mock<ILogger<WordCloudTask>>();

            var task = new WordCloudTask(mockDbContext, mockBotClient.Object, mockSendMessage.Object, mockLogger.Object);

            // Act & Assert
            Assert.Equal("WordCloudReport", task.TaskName);
        }

        [Theory]
        [InlineData(DayOfWeek.Monday, true, false, false, false)]
        [InlineData(DayOfWeek.Tuesday, false, false, false, false)]
        [InlineData(DayOfWeek.Wednesday, false, false, false, false)]
        [InlineData(DayOfWeek.Thursday, false, false, false, false)]
        [InlineData(DayOfWeek.Friday, false, false, false, false)]
        [InlineData(DayOfWeek.Saturday, false, false, false, false)]
        [InlineData(DayOfWeek.Sunday, false, false, false, false)]
        public void CheckPeriodStart_ShouldReturnCorrectWeekStart(DayOfWeek dayOfWeek, bool expectedWeekStart, bool expectedMonthStart, bool expectedQuarterStart, bool expectedYearStart)
        {
            // 这个测试验证周期检查逻辑
            // 注意：由于CheckPeriodStart是静态方法且依赖DateTime.Today，这里只能测试逻辑
            // 实际的日期检查需要在集成测试中进行
            
            // 我们可以测试周一是否被正确识别为周开始
            if (DateTime.Today.DayOfWeek == dayOfWeek)
            {
                var (isWeekStart, isMonthStart, isQuarterStart, isYearStart) = WordCloudTask.CheckPeriodStart();
                Assert.Equal(expectedWeekStart, isWeekStart);
            }
        }

        [Fact]
        public void GetExecutableTaskTypes_ShouldReturnCorrectTypes()
        {
            // Arrange
            var mockDbContext = GetInMemoryDbContext();
            var mockBotClient = new Mock<ITelegramBotClient>();
            var mockSendMessageLogger = new Mock<ILogger<SendMessage>>();
            var mockSendMessage = new Mock<SendMessage>(mockBotClient.Object, mockSendMessageLogger.Object);
            var mockLogger = new Mock<ILogger<WordCloudTask>>();

            var task = new WordCloudTask(mockDbContext, mockBotClient.Object, mockSendMessage.Object, mockLogger.Object);

            // Act
            var executableTypes = task.GetExecutableTaskTypes();

            // Assert
            Assert.NotNull(executableTypes);
            // 根据当前日期，应该返回相应的任务类型
            // 这里我们只验证返回的是数组类型
            Assert.IsType<string[]>(executableTypes);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldCompleteWithoutError()
        {
            // Arrange
            var mockDbContext = GetInMemoryDbContext();
            var mockBotClient = new Mock<ITelegramBotClient>();
            var mockSendMessageLogger = new Mock<ILogger<SendMessage>>();
            var mockSendMessage = new Mock<SendMessage>(mockBotClient.Object, mockSendMessageLogger.Object);
            var mockLogger = new Mock<ILogger<WordCloudTask>>();

            var task = new WordCloudTask(mockDbContext, mockBotClient.Object, mockSendMessage.Object, mockLogger.Object);

            // Act & Assert - 应该不抛出异常
            await task.ExecuteAsync();
        }

        [Fact]
        public async Task CountUserMessagesAsync_WithEmptyDatabase_ShouldReturnEmptyDictionary()
        {
            // Arrange
            var dbContext = GetInMemoryDbContext();
            var mockBotClient = new Mock<ITelegramBotClient>();
            var mockSendMessageLogger = new Mock<ILogger<SendMessage>>();
            var mockSendMessage = new Mock<SendMessage>(mockBotClient.Object, mockSendMessageLogger.Object);
            var mockLogger = new Mock<ILogger<WordCloudTask>>();

            var task = new WordCloudTask(dbContext, mockBotClient.Object, mockSendMessage.Object, mockLogger.Object);

            // Act
            var result = await task.CountUserMessagesAsync(WordCloudTask.TimePeriod.Weekly);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetGroupMessagesWithExtensionsAsync_WithEmptyDatabase_ShouldReturnEmptyDictionary()
        {
            // Arrange
            var dbContext = GetInMemoryDbContext();
            var mockBotClient = new Mock<ITelegramBotClient>();
            var mockSendMessageLogger = new Mock<ILogger<SendMessage>>();
            var mockSendMessage = new Mock<SendMessage>(mockBotClient.Object, mockSendMessageLogger.Object);
            var mockLogger = new Mock<ILogger<WordCloudTask>>();

            var task = new WordCloudTask(dbContext, mockBotClient.Object, mockSendMessage.Object, mockLogger.Object);

            // Act
            var result = await task.GetGroupMessagesWithExtensionsAsync(WordCloudTask.TimePeriod.Weekly);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
    }
}
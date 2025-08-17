using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot.Types;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Domain.Tests
{
    public abstract class TestBase
    {
        protected Mock<ILogger<T>> CreateLoggerMock<T>() where T : class
        {
            return new Mock<ILogger<T>>();
        }
        
        protected Mock<DataDbContext> CreateMockDbContext()
        {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new Mock<DataDbContext>(options);
        }
        
        protected Mock<ITelegramBotClient> CreateMockBotClient()
        {
            return new Mock<ITelegramBotClient>();
        }

        protected static Mock<DbSet<T>> CreateMockDbSet<T>(IEnumerable<T> data) where T : class
        {
            var mockSet = new Mock<DbSet<T>>();
            var queryable = data.AsQueryable();
            
            mockSet.As<IQueryable<T>>().Setup(m => m.Provider).Returns(queryable.Provider);
            mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
            mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
            mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
            
            return mockSet;
        }
    }

    public abstract class MessageServiceTestBase : TestBase
    {
        protected MessageService CreateService(
            DataDbContext? dbContext = null,
            ILogger<MessageService>? logger = null,
            LuceneManager? luceneManager = null,
            SendMessage? sendMessage = null,
            IMediator? mediator = null)
        {
            return new MessageService(
                logger ?? CreateLoggerMock<MessageService>().Object,
                luceneManager ?? new Mock<LuceneManager>(Mock.Of<SendMessage>()).Object,
                sendMessage ?? new Mock<SendMessage>(Mock.Of<ITelegramBotClient>(), Mock.Of<ILogger<SendMessage>>()).Object,
                dbContext ?? CreateMockDbContext().Object,
                mediator ?? Mock.Of<IMediator>());
        }
    }
}
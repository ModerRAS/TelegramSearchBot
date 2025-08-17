using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
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
            var dataList = data.ToList();
            
            // 设置查询操作
            mockSet.As<IQueryable<T>>().Setup(m => m.Provider).Returns(queryable.Provider);
            mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
            mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
            mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
            
            // 设置异步操作
            mockSet.As<IAsyncEnumerable<T>>()
                .Setup(m => m.GetAsyncEnumerator())
                .Returns(new TestAsyncEnumerator<T>(dataList.GetEnumerator()));
            
            mockSet.As<IQueryable<T>>()
                .Setup(m => m.Provider)
                .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
            
            // 设置添加操作
            mockSet.Setup(m => m.Add(It.IsAny<T>())).Callback<T>(dataList.Add);
            mockSet.Setup(m => m.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
                .Callback<T, CancellationToken>((entity, token) => dataList.Add(entity))
                .ReturnsAsync((T entity, CancellationToken token) => 
                {
                    // 简化实现，直接返回实体
                    return entity;
                });
            
            // 设置删除操作
            mockSet.Setup(m => m.Remove(It.IsAny<T>())).Callback<T>(entity => dataList.Remove(entity));
            
            // 设置查找操作
            mockSet.Setup(m => m.Find(It.IsAny<object[]>()))
                .Returns<object[]>(keys => 
                {
                    // 简化实现，假设第一个键是ID
                    if (keys.Length > 0 && keys[0] is long id)
                    {
                        return dataList.FirstOrDefault(d => 
                        {
                            var idProperty = d.GetType().GetProperty("Id");
                            return idProperty != null && (long)idProperty.GetValue(d) == id;
                        });
                    }
                    return null;
                });
            
            return mockSet;
        }
        
        // 异步枚举器实现
        private class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
        {
            private readonly IEnumerator<T> _enumerator;
            
            public TestAsyncEnumerator(IEnumerator<T> enumerator)
            {
                _enumerator = enumerator;
            }
            
            public T Current => _enumerator.Current;
            
            public ValueTask DisposeAsync()
            {
                _enumerator.Dispose();
                return ValueTask.CompletedTask;
            }
            
            public ValueTask<bool> MoveNextAsync()
            {
                return ValueTask.FromResult(_enumerator.MoveNext());
            }
        }
        
        // 异步查询提供者实现
        private class TestAsyncQueryProvider<T> : IAsyncQueryProvider
        {
            private readonly IQueryProvider _provider;
            
            public TestAsyncQueryProvider(IQueryProvider provider)
            {
                _provider = provider;
            }
            
            public IQueryable CreateQuery<TElement>(Expression expression)
            {
                return new TestAsyncQueryable<TElement>(expression);
            }
            
            public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
            {
                return new TestAsyncQueryable<TElement>(expression);
            }
            
            public object? Execute(Expression expression)
            {
                return _provider.Execute(expression);
            }
            
            public TResult Execute<TResult>(Expression expression)
            {
                return _provider.Execute<TResult>(expression);
            }
            
            public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
            {
                // 简化实现，直接同步执行
                var resultType = typeof(TResult);
                if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var innerResult = _provider.Execute(expression);
                    return (TResult)Task.FromResult(innerResult);
                }
                else if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                {
                    var innerResult = _provider.Execute(expression);
                    return (TResult)(object)new ValueTask<object>(innerResult);
                }
                else
                {
                    return _provider.Execute<TResult>(expression);
                }
            }
        }
        
        // 异步查询实现
        private class TestAsyncQueryable<T> : IQueryable<T>
        {
            public Type ElementType => typeof(T);
            public Expression Expression { get; }
            public IQueryProvider Provider { get; }
            
            public TestAsyncQueryable(Expression expression)
            {
                Expression = expression;
                Provider = new TestAsyncQueryProvider<T>(new TestQueryProvider());
            }
            
            public IEnumerator<T> GetEnumerator()
            {
                return Provider.Execute<IEnumerable<T>>(Expression).GetEnumerator();
            }
            
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
        
        // 查询提供者实现
        private class TestQueryProvider : IQueryProvider
        {
            public IQueryable CreateQuery(Expression expression)
            {
                var elementType = expression.Type.GetGenericArguments()[0];
                var queryableType = typeof(TestAsyncQueryable<>).MakeGenericType(elementType);
                return (IQueryable)Activator.CreateInstance(queryableType, expression);
            }
            
            public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
            {
                return new TestAsyncQueryable<TElement>(expression);
            }
            
            public object? Execute(Expression expression)
            {
                // 简化实现，返回默认值
                return expression.Type.IsValueType ? Activator.CreateInstance(expression.Type) : null;
            }
            
            public TResult Execute<TResult>(Expression expression)
            {
                // 简化实现
                if (typeof(TResult) == typeof(int))
                {
                    return (TResult)(object)0;
                }
                return default(TResult);
            }
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
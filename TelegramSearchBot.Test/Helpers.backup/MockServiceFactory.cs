using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.AI.Interface.LLM;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Common.Interface.AI;
using TelegramSearchBot.Common.Interface.Bilibili;
using TelegramSearchBot.Common.Interface.Vector;
using TelegramSearchBot.Common.Model;
using TelegramSearchBot.Common.Model.PipelineContext;
using TelegramSearchBot.Common.Model;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Service;
using TelegramSearchBot.Service.BotAPI;

namespace TelegramSearchBot.Test.Helpers
{
    /// <summary>
    /// Mock对象工厂，提供统一的Mock对象创建接口
    /// </summary>
    public static class MockServiceFactory
    {
        #region Telegram Bot Client Mocks

        /// <summary>
        /// 创建TelegramBotClient的Mock对象
        /// </summary>
        /// <param name="configure">配置Mock对象的回调</param>
        /// <returns>Mock的ITelegramBotClient</returns>
        public static Mock<ITelegramBotClient> CreateTelegramBotClientMock(Action<Mock<ITelegramBotClient>>? configure = null)
        {
            var mock = new Mock<ITelegramBotClient>();

            // 默认配置
            mock.Setup(x => x.BotId).Returns(123456789);
            mock.Setup(x => x.GetMeAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new User
                {
                    Id = 123456789,
                    FirstName = "Test",
                    LastName = "Bot",
                    Username = "testbot",
                    IsBot = true
                });

            configure?.Invoke(mock);
            return mock;
        }

        /// <summary>
        /// 创建配置了SendMessage行为的TelegramBotClient Mock
        /// </summary>
        /// <param name="expectedMessage">期望发送的消息内容</param>
        /// <param name="chatId">目标聊天ID</param>
        /// <returns>Mock的ITelegramBotClient</returns>
        public static Mock<ITelegramBotClient> CreateTelegramBotClientWithSendMessage(string expectedMessage, long chatId)
        {
            var mock = CreateTelegramBotClientMock();
            
            mock.Setup(x => x.SendMessageAsync(
                    It.Is<long>(id => id == chatId),
                    It.Is<string>(msg => msg == expectedMessage),
                    It.IsAny<ParseMode>(),
                    It.IsAny<IEnumerable<MessageEntity>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<IReplyMarkup>(),
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(new Message
                {
                    MessageId = 12345,
                    Text = expectedMessage,
                    Chat = new Chat { Id = chatId },
                    Date = DateTime.UtcNow
                });

            return mock;
        }

        /// <summary>
        /// 创建配置了GetFile行为的TelegramBotClient Mock
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="fileStream">文件流</param>
        /// <returns>Mock的ITelegramBotClient</returns>
        public static Mock<ITelegramBotClient> CreateTelegramBotClientWithGetFile(string filePath, Stream fileStream)
        {
            var mock = CreateTelegramBotClientMock();
            
            mock.Setup(x => x.GetFileAsync(
                    It.Is<string>(path => path == filePath),
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(new Telegram.Bot.Types.File
                {
                    FilePath = filePath,
                    FileSize = (int)fileStream.Length,
                    FileId = "test-file-id"
                });

            mock.Setup(x => x.DownloadFileAsync(
                    It.Is<string>(path => path == filePath),
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(fileStream);

            return mock;
        }

        #endregion

        #region LLM Service Mocks

        /// <summary>
        ///创建通用LLM服务的Mock对象
        /// </summary>
        /// <param name="configure">配置Mock对象的回调</param>
        /// <returns>Mock的IGeneralLLMService</returns>
        public static Mock<IGeneralLLMService> CreateLLMServiceMock(Action<Mock<IGeneralLLMService>>? configure = null)
        {
            var mock = new Mock<IGeneralLLMService>();

            // 默认配置
            mock.Setup(x => x.GetModelName()).Returns("test-model");
            mock.Setup(x => x.GetProvider()).Returns(LLMProvider.OpenAI);
            mock.Setup(x => x.GetMaxTokens()).Returns(4096);
            mock.Setup(x => x.IsAvailable()).Returns(true);

            configure?.Invoke(mock);
            return mock;
        }

        /// <summary>
        /// 创建配置了ChatCompletion行为的LLM服务Mock
        /// </summary>
        /// <param name="response">响应内容</param>
        /// <param name="delay">响应延迟</param>
        /// <returns>Mock的IGeneralLLMService</returns>
        public static Mock<IGeneralLLMService> CreateLLMServiceWithChatCompletion(string response, TimeSpan? delay = null)
        {
            var mock = CreateLLMServiceMock();
            
            mock.Setup(x => x.ChatCompletionAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(response);

            if (delay.HasValue)
            {
                mock.Setup(x => x.ChatCompletionAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()
                    ))
                    .Returns(async () =>
                    {
                        await Task.Delay(delay.Value);
                        return response;
                    });
            }

            return mock;
        }

        /// <summary>
        /// 创建配置了Embedding行为的LLM服务Mock
        /// </summary>
        /// <param name="vectors">向量数组</param>
        /// <returns>Mock的IGeneralLLMService</returns>
        public static Mock<IGeneralLLMService> CreateLLMServiceWithEmbedding(float[][] vectors)
        {
            var mock = CreateLLMServiceMock();
            
            mock.Setup(x => x.GetEmbeddingAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync((string text, CancellationToken token) => 
                {
                    // 简化实现：根据文本长度选择向量
                    var index = Math.Min(text.Length, vectors.Length - 1);
                    return vectors[index];
                });

            return mock;
        }

        /// <summary>
        /// 创建会抛出异常的LLM服务Mock
        /// </summary>
        /// <param name="exception">要抛出的异常</param>
        /// <returns>Mock的IGeneralLLMService</returns>
        public static Mock<IGeneralLLMService> CreateLLMServiceWithException(Exception exception)
        {
            var mock = CreateLLMServiceMock();
            
            mock.Setup(x => x.ChatCompletionAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ))
                .ThrowsAsync(exception);

            return mock;
        }

        #endregion

        #region Logger Mocks

        /// <summary>
        /// 创建Logger的Mock对象
        /// </summary>
        /// <typeparam name="T">日志类型</typeparam>
        /// <param name="configure">配置Mock对象的回调</param>
        /// <returns>Mock的ILogger</returns>
        public static Mock<ILogger<T>> CreateLoggerMock<T>(Action<Mock<ILogger<T>>>? configure = null)
        {
            var mock = new Mock<ILogger<T>>();
            
            // 默认配置：所有日志级别都启用
            mock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            configure?.Invoke(mock);
            return mock;
        }

        /// <summary>
        /// 创建记录特定日志的Logger Mock
        /// </summary>
        /// <typeparam name="T">日志类型</typeparam>
        /// <param name="expectedLogLevel">期望的日志级别</param>
        /// <param name="expectedMessage">期望的日志消息</param>
        /// <returns>Mock的ILogger</returns>
        public static Mock<ILogger<T>> CreateLoggerWithExpectedLog<T>(LogLevel expectedLogLevel, string expectedMessage)
        {
            var mock = CreateLoggerMock<T>();
            
            mock.Setup(x => x.Log(
                    expectedLogLevel,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(expectedMessage)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ));

            return mock;
        }

        #endregion

        #region HttpClient Mocks

        /// <summary>
        /// 创建HttpClient的Mock对象
        /// </summary>
        /// <param name="configure">配置Mock对象的回调</param>
        /// <returns>Mock的HttpMessageHandler</returns>
        public static Mock<HttpMessageHandler> CreateHttpMessageHandlerMock(Action<Mock<HttpMessageHandler>>? configure = null)
        {
            var mock = new Mock<HttpMessageHandler>();

            configure?.Invoke(mock);
            return mock;
        }

        /// <summary>
        /// 创建配置了响应的HttpClient Mock
        /// </summary>
        /// <param name="responseMessage">响应消息</param>
        /// <returns>HttpClient实例</returns>
        public static HttpClient CreateHttpClientWithResponse(HttpResponseMessage responseMessage)
        {
            var mockHandler = CreateHttpMessageHandlerMock();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(responseMessage);

            return new HttpClient(mockHandler.Object);
        }

        /// <summary>
        /// 创建配置了JSON响应的HttpClient Mock
        /// </summary>
        /// <typeparam name="T">JSON数据类型</typeparam>
        /// <param name="responseData">响应数据</param>
        /// <param name="statusCode">HTTP状态码</param>
        /// <returns>HttpClient实例</returns>
        public static HttpClient CreateHttpClientWithJsonResponse<T>(T responseData, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(statusCode);
            response.Content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(responseData));
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            return CreateHttpClientWithResponse(response);
        }

        #endregion

        #region Database Mocks

        /// <summary>
        /// 创建DbContext的Mock对象
        /// </summary>
        /// <param name="configure">配置Mock对象的回调</param>
        /// <returns>Mock的DataDbContext</returns>
        public static Mock<DataDbContext> CreateDbContextMock(Action<Mock<DataDbContext>>? configure = null)
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            
            var mock = new Mock<DataDbContext>(options);
            
            configure?.Invoke(mock);
            return mock;
        }

        /// <summary>
        /// 创建包含数据的DbContext Mock
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="data">数据集合</param>
        /// <returns>Mock的DataDbContext</returns>
        public static Mock<DataDbContext> CreateDbContextWithData<T>(IEnumerable<T> data) where T : class
        {
            var mock = CreateDbContextMock();
            var mockSet = CreateMockDbSet(data);
            
            // 根据实体类型设置对应的DbSet
            if (typeof(T) == typeof(Message))
            {
                mock.Setup(x => x.Messages).Returns(mockSet.As<DbSet<Message>>());
            }
            else if (typeof(T) == typeof(UserData))
            {
                mock.Setup(x => x.UserData).Returns(mockSet.As<DbSet<UserData>>());
            }
            else if (typeof(T) == typeof(GroupData))
            {
                mock.Setup(x => x.GroupData).Returns(mockSet.As<DbSet<GroupData>>());
            }
            // 可以添加更多实体类型的支持

            return mock;
        }

        /// <summary>
        /// 创建DbSet的Mock对象
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="data">数据集合</param>
        /// <returns>Mock的DbSet</returns>
        public static Mock<DbSet<T>> CreateMockDbSet<T>(IEnumerable<T> data) where T : class
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
                .ReturnsAsync((T entity, CancellationToken token) => entity);

            // 设置删除操作
            mockSet.Setup(m => m.Remove(It.IsAny<T>())).Callback<T>(entity => dataList.Remove(entity));

            return mockSet;
        }

        #endregion

        #region Service Mocks

        /// <summary>
        /// 创建SendMessage服务的Mock对象
        /// </summary>
        /// <param name="configure">配置Mock对象的回调</param>
        /// <returns>Mock的SendMessage</returns>
        public static Mock<SendMessage> CreateSendMessageMock(Action<Mock<SendMessage>>? configure = null)
        {
            var mockBotClient = CreateTelegramBotClientMock();
            var mockLogger = CreateLoggerMock<SendMessage>();
            
            var mock = new Mock<SendMessage>(mockBotClient.Object, mockLogger.Object);
            
            configure?.Invoke(mock);
            return mock;
        }

        /// <summary>
        /// 创建LuceneManager的Mock对象
        /// </summary>
        /// <param name="configure">配置Mock对象的回调</param>
        /// <returns>Mock的LuceneManager</returns>
        public static Mock<LuceneManager> CreateLuceneManagerMock(Action<Mock<LuceneManager>>? configure = null)
        {
            var mockSendMessage = CreateSendMessageMock();
            var mock = new Mock<LuceneManager>(mockSendMessage.Object);
            
            configure?.Invoke(mock);
            return mock;
        }

        /// <summary>
        /// 创建Mediator的Mock对象
        /// </summary>
        /// <param name="configure">配置Mock对象的回调</param>
        /// <returns>Mock的IMediator</returns>
        public static Mock<MediatR.IMediator> CreateMediatorMock(Action<Mock<MediatR.IMediator>>? configure = null)
        {
            var mock = new Mock<MediatR.IMediator>();
            
            // 默认配置：所有发送都返回成功
            mock.Setup(x => x.Send(It.IsAny<MediatR.IRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MediatR.Unit.Value);

            configure?.Invoke(mock);
            return mock;
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// 测试用异步枚举器
        /// </summary>
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

        /// <summary>
        /// 测试用异步查询提供者
        /// </summary>
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

        /// <summary>
        /// 测试用异步查询
        /// </summary>
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

        /// <summary>
        /// 测试用查询提供者
        /// </summary>
        private class TestQueryProvider : IQueryProvider
        {
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
                if (expression.Type == typeof(IEnumerable<T>))
                {
                    return Enumerable.Empty<T>();
                }
                return null;
            }

            public TResult Execute<TResult>(Expression expression)
            {
                if (typeof(TResult) == typeof(IEnumerable<T>))
                {
                    return (TResult)(object)Enumerable.Empty<T>();
                }
                return default(TResult);
            }
        }

        #endregion
    }
}
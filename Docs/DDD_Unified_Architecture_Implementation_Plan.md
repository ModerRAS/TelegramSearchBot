# TelegramSearchBot项目DDD架构统一实施方案

## 概述

基于详细的架构冲突分析，本方案提供了具体的实施步骤，帮助TelegramSearchBot项目实现Message领域DDD统一架构，解决386个编译错误，确保系统平滑过渡。

## 第一阶段：基础设施搭建（1-2周）

### 1.1 创建适配器层

#### 1.1.1 实现MessageRepositoryAdapter

```csharp
// 文件路径：TelegramSearchBot.Application/Adapters/MessageRepositoryAdapter.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Application.Adapters
{
    /// <summary>
    /// Message仓储适配器，用于桥接DDD仓储接口和现有代码
    /// </summary>
    public class MessageRepositoryAdapter : IMessageRepositoryAdapter
    {
        private readonly IMessageRepository _dddRepository;
        private readonly IMapper _mapper;

        public MessageRepositoryAdapter(IMessageRepository dddRepository, IMapper mapper)
        {
            _dddRepository = dddRepository ?? throw new ArgumentNullException(nameof(dddRepository));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        #region DDD仓储接口实现

        public async Task<MessageAggregate> GetByIdAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.GetByIdAsync(id, cancellationToken);
        }

        public async Task<IEnumerable<MessageAggregate>> GetByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.GetByGroupIdAsync(groupId, cancellationToken);
        }

        public async Task<MessageAggregate> AddAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.AddAsync(aggregate, cancellationToken);
        }

        public async Task UpdateAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            await _dddRepository.UpdateAsync(aggregate, cancellationToken);
        }

        public async Task DeleteAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            await _dddRepository.DeleteAsync(id, cancellationToken);
        }

        public async Task<bool> ExistsAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.ExistsAsync(id, cancellationToken);
        }

        public async Task<int> CountByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.CountByGroupIdAsync(groupId, cancellationToken);
        }

        public async Task<IEnumerable<MessageAggregate>> SearchAsync(long groupId, string query, int limit = 50, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.SearchAsync(groupId, query, limit, cancellationToken);
        }

        #endregion

        #region 兼容旧代码的方法实现

        public async Task<long> AddMessageAsync(Message message)
        {
            var aggregate = _mapper.Map<MessageAggregate>(message);
            var result = await _dddRepository.AddAsync(aggregate);
            return result.Id.TelegramMessageId;
        }

        public async Task<Message> GetMessageByIdAsync(long id)
        {
            // 需要根据实际情况构造MessageId
            var messageId = new MessageId(0, id); // 注意：这可能需要调整
            var aggregate = await _dddRepository.GetByIdAsync(messageId);
            return _mapper.Map<Message>(aggregate);
        }

        public async Task<List<Message>> GetMessagesByGroupIdAsync(long groupId)
        {
            var aggregates = await _dddRepository.GetByGroupIdAsync(groupId);
            return aggregates.Select(a => _mapper.Map<Message>(a)).ToList();
        }

        public async Task<List<Message>> SearchMessagesAsync(string query, long groupId)
        {
            var aggregates = await _dddRepository.SearchAsync(groupId, query);
            return aggregates.Select(a => _mapper.Map<Message>(a)).ToList();
        }

        public async Task<List<Message>> GetMessagesByUserAsync(long userId)
        {
            // 实现用户消息查询逻辑
            var allMessages = await _dddRepository.GetByGroupIdAsync(0); // 需要调整
            return allMessages
                .Where(m => m.Metadata.FromUserId == userId)
                .Select(a => _mapper.Map<Message>(a))
                .ToList();
        }

        #endregion
    }
}
```

#### 1.1.2 创建适配器接口

```csharp
// 文件路径：TelegramSearchBot.Application/Adapters/IMessageRepositoryAdapter.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Application.Adapters
{
    /// <summary>
    /// Message仓储适配器接口，提供DDD仓储和传统仓储的统一访问
    /// </summary>
    public interface IMessageRepositoryAdapter
    {
        // DDD仓储接口方法
        Task<MessageAggregate> GetByIdAsync(MessageId id, CancellationToken cancellationToken = default);
        Task<IEnumerable<MessageAggregate>> GetByGroupIdAsync(long groupId, CancellationToken cancellationToken = default);
        Task<MessageAggregate> AddAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default);
        Task UpdateAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default);
        Task DeleteAsync(MessageId id, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(MessageId id, CancellationToken cancellationToken = default);
        Task<int> CountByGroupIdAsync(long groupId, CancellationToken cancellationToken = default);
        Task<IEnumerable<MessageAggregate>> SearchAsync(long groupId, string query, int limit = 50, CancellationToken cancellationToken = default);

        // 兼容旧代码的方法
        Task<long> AddMessageAsync(Message message);
        Task<Message> GetMessageByIdAsync(long id);
        Task<List<Message>> GetMessagesByGroupIdAsync(long groupId);
        Task<List<Message>> SearchMessagesAsync(string query, long groupId);
        Task<List<Message>> GetMessagesByUserAsync(long userId);
    }
}
```

### 1.2 配置AutoMapper

#### 1.2.1 创建映射配置

```csharp
// 文件路径：TelegramSearchBot.Application/Mappings/MessageMappingProfile.cs
using AutoMapper;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Application.Mappings
{
    /// <summary>
    /// Message对象映射配置
    /// </summary>
    public class MessageMappingProfile : Profile
    {
        public MessageMappingProfile()
        {
            // MessageAggregate 到 Message 的映射
            CreateMap<MessageAggregate, Message>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.TelegramMessageId))
                .ForMember(dest => dest.GroupId, opt => opt.MapFrom(src => src.Id.ChatId))
                .ForMember(dest => dest.MessageId, opt => opt.MapFrom(src => src.Id.TelegramMessageId))
                .ForMember(dest => dest.FromUserId, opt => opt.MapFrom(src => src.Metadata.FromUserId))
                .ForMember(dest => dest.ReplyToUserId, opt => opt.MapFrom(src => src.Metadata.ReplyToUserId))
                .ForMember(dest => dest.ReplyToMessageId, opt => opt.MapFrom(src => src.Metadata.ReplyToMessageId))
                .ForMember(dest => dest.Content, opt => opt.MapFrom(src => src.Content.Text))
                .ForMember(dest => dest.DateTime, opt => opt.MapFrom(src => src.Metadata.Timestamp));

            // Message 到 MessageAggregate 的映射
            CreateMap<Message, MessageAggregate>()
                .ConstructUsing(src => MessageAggregate.Create(
                    src.GroupId,
                    src.MessageId,
                    src.Content,
                    src.FromUserId,
                    src.ReplyToUserId,
                    src.ReplyToMessageId,
                    src.DateTime));

            // MessageOption 到 MessageAggregate 的映射
            CreateMap<MessageOption, MessageAggregate>()
                .ConstructUsing(src => MessageAggregate.Create(
                    src.ChatId,
                    src.MessageId,
                    src.Content,
                    src.UserId,
                    src.DateTime));

            // 其他相关映射...
            CreateMap<MessageExtension, MessageExtension>();
            CreateMap<MessageContent, string>()
                .ConvertUsing(src => src.Text);
        }
    }
}
```

### 1.3 统一依赖注入配置

#### 1.3.1 更新ServiceCollectionExtension

```csharp
// 文件路径：TelegramSearchBot.Infrastructure/Extension/ServiceCollectionExtension.cs
using System.Reflection;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Application.Adapters;
using TelegramSearchBot.Application.Mappings;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;

namespace TelegramSearchBot.Infrastructure.Extension
{
    public static class ServiceCollectionExtension
    {
        public static IServiceCollection AddUnifiedArchitectureServices(this IServiceCollection services)
        {
            var connectionString = $"Data Source={Env.WorkDir}/Data.sqlite;Cache=Shared;Mode=ReadWriteCreate;";
            
            // 基础设施服务
            services.AddDbContext<DataDbContext>(options =>
                options.UseSqlite(connectionString), ServiceLifetime.Scoped);

            // DDD仓储
            services.AddScoped<IMessageRepository, MessageRepository>();

            // 适配器
            services.AddScoped<IMessageRepositoryAdapter, MessageRepositoryAdapter>();

            // AutoMapper
            services.AddAutoMapper(cfg =>
            {
                cfg.AddProfile<MessageMappingProfile>();
            });

            // 其他服务...
            services.AddTelegramBotClient();
            services.AddRedis();
            services.AddHttpClients();
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            });

            return services;
        }

        public static IServiceCollection AddTelegramBotClient(this IServiceCollection services)
        {
            return services.AddSingleton<ITelegramBotClient>(sp => 
                new TelegramBotClient(Env.BotToken));
        }

        public static IServiceCollection AddRedis(this IServiceCollection services)
        {
            var redisConnectionString = $"localhost:{Env.SchedulerPort}";
            return services.AddSingleton<IConnectionMultiplexer>(
                ConnectionMultiplexer.Connect(redisConnectionString));
        }

        public static IServiceCollection AddHttpClients(this IServiceCollection services)
        {
            services.AddHttpClient("BiliApiClient");
            services.AddHttpClient(string.Empty);
            return services;
        }
    }
}
```

## 第二阶段：核心功能迁移（2-3周）

### 2.1 修复编译错误

#### 2.1.1 更新测试项目引用

```csharp
// 文件路径：TelegramSearchBot.Test/Benchmarks/Domain/Message/MessageRepositoryBenchmarks.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Application.Adapters;
using TelegramSearchBot.Model.Data;
using DMessageRepository = TelegramSearchBot.Domain.Message.MessageRepository;

namespace TelegramSearchBot.Test.Benchmarks.Domain.Message
{
    public class MessageRepositoryBenchmarks
    {
        private readonly IMessageRepositoryAdapter _adapter;
        private readonly DataDbContext _context;
        private readonly List<Message> _testMessages;

        public MessageRepositoryBenchmarks()
        {
            // 使用依赖注入或手动创建服务
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase($"BenchmarkDb_{Guid.NewGuid()}")
                .Options;
            
            _context = new DataDbContext(options);
            var dddRepository = new DMessageRepository(_context, null);
            var mapper = CreateMapper();
            
            _adapter = new MessageRepositoryAdapter(dddRepository, mapper);
            
            // 创建测试数据
            _testMessages = CreateTestMessages();
        }

        [Benchmark]
        public async Task AddMessageAsync()
        {
            var message = _testMessages.First();
            await _adapter.AddMessageAsync(message);
        }

        [Benchmark]
        public async Task GetMessagesByGroupIdAsync()
        {
            var groupId = 100;
            await _adapter.GetMessagesByGroupIdAsync(groupId);
        }

        [Benchmark]
        public async Task SearchMessagesAsync()
        {
            var query = "test";
            var groupId = 100;
            await _adapter.SearchMessagesAsync(query, groupId);
        }

        // ... 其他基准测试方法

        private IMapper CreateMapper()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.AddProfile<MessageMappingProfile>();
            });
            return config.CreateMapper();
        }

        private List<Message> CreateTestMessages()
        {
            return new List<Message>
            {
                new Message
                {
                    GroupId = 100,
                    MessageId = 1,
                    FromUserId = 1,
                    Content = "Test message 1",
                    DateTime = DateTime.UtcNow
                },
                // ... 更多测试消息
            };
        }
    }
}
```

#### 2.1.2 修复MessageExtension属性访问

```csharp
// 文件路径：TelegramSearchBot.Test/Domain/Message/MessageEntityTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Test.Domain.Message
{
    public class MessageEntityTests
    {
        [Fact]
        public void Message_WithExtensions_ShouldWorkCorrectly()
        {
            // Arrange
            var message = new Message
            {
                GroupId = 100,
                MessageId = 1,
                FromUserId = 1,
                Content = "Test message",
                DateTime = DateTime.UtcNow,
                MessageExtensions = new List<MessageExtension>
                {
                    new MessageExtension
                    {
                        ExtensionType = "OCR",
                        ExtensionData = "OCR result text"
                    }
                }
            };

            // Act & Assert
            Assert.NotNull(message.MessageExtensions);
            Assert.Single(message.MessageExtensions);
            
            var extension = message.MessageExtensions.First();
            Assert.Equal("OCR", extension.ExtensionType);
            Assert.Equal("OCR result text", extension.ExtensionData);
        }

        [Fact]
        public void Message_Constructor_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var message = new Message();

            // Assert
            Assert.Equal(0, message.Id);
            Assert.Equal(default(DateTime), message.DateTime);
            Assert.Equal(0, message.GroupId);
            Assert.Equal(0, message.MessageId);
            Assert.Null(message.Content);
            Assert.NotNull(message.MessageExtensions);
        }
    }
}
```

### 2.2 更新服务层

#### 2.2.1 修改MessageService

```csharp
// 文件路径：TelegramSearchBot.Domain/Message/MessageService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Domain.Message
{
    /// <summary>
    /// Message领域服务，处理消息的业务逻辑
    /// </summary>
    public class MessageService : IMessageService
    {
        private readonly IMessageRepository _messageRepository;
        private readonly ILogger<MessageService> _logger;

        public MessageService(IMessageRepository messageRepository, ILogger<MessageService> logger)
        {
            _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 处理传入的消息
        /// </summary>
        public async Task<long> ProcessMessageAsync(MessageOption messageOption)
        {
            try
            {
                if (messageOption == null)
                    throw new ArgumentNullException(nameof(messageOption));

                if (!ValidateMessageOption(messageOption))
                    throw new ArgumentException("Invalid message option data", nameof(messageOption));

                // 转换为DDD聚合
                var messageAggregate = MessageAggregate.Create(
                    messageOption.ChatId,
                    messageOption.MessageId,
                    messageOption.Content,
                    messageOption.UserId,
                    messageOption.DateTime);

                // 保存到仓储
                var savedAggregate = await _messageRepository.AddAsync(messageAggregate);

                _logger.LogInformation("Processed message {MessageId} from user {UserId} in group {GroupId}", 
                    messageOption.MessageId, messageOption.UserId, messageOption.ChatId);

                return savedAggregate.Id.TelegramMessageId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message {MessageId}", messageOption.MessageId);
                throw;
            }
        }

        /// <summary>
        /// 根据ID获取消息
        /// </summary>
        public async Task<Message> GetMessageByIdAsync(long id)
        {
            try
            {
                var messageId = new MessageId(0, id); // 注意：可能需要调整
                var aggregate = await _messageRepository.GetByIdAsync(messageId);
                
                if (aggregate == null)
                    return null;

                return MapToMessage(aggregate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting message by ID {MessageId}", id);
                throw;
            }
        }

        /// <summary>
        /// 获取群组消息列表
        /// </summary>
        public async Task<List<Message>> GetMessagesByGroupIdAsync(long groupId)
        {
            try
            {
                var aggregates = await _messageRepository.GetByGroupIdAsync(groupId);
                return aggregates.Select(MapToMessage).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for group {GroupId}", groupId);
                throw;
            }
        }

        /// <summary>
        /// 搜索消息
        /// </summary>
        public async Task<List<Message>> SearchMessagesAsync(string query, long groupId)
        {
            try
            {
                var aggregates = await _messageRepository.SearchAsync(groupId, query);
                return aggregates.Select(MapToMessage).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching messages in group {GroupId}", groupId);
                throw;
            }
        }

        #region Private Methods

        private bool ValidateMessageOption(MessageOption messageOption)
        {
            return messageOption.ChatId > 0 &&
                   messageOption.MessageId > 0 &&
                   messageOption.UserId > 0 &&
                   !string.IsNullOrEmpty(messageOption.Content);
        }

        private Message MapToMessage(MessageAggregate aggregate)
        {
            return new Message
            {
                Id = aggregate.Id.TelegramMessageId,
                GroupId = aggregate.Id.ChatId,
                MessageId = aggregate.Id.TelegramMessageId,
                FromUserId = aggregate.Metadata.FromUserId,
                ReplyToUserId = aggregate.Metadata.ReplyToUserId,
                ReplyToMessageId = aggregate.Metadata.ReplyToMessageId,
                Content = aggregate.Content.Text,
                DateTime = aggregate.Metadata.Timestamp,
                MessageExtensions = new List<MessageExtension>()
            };
        }

        #endregion
    }
}
```

## 第三阶段：DDD优化（1-2周）

### 3.1 引入领域事件

#### 3.1.1 实现领域事件发布

```csharp
// 文件路径：TelegramSearchBot.Domain/Message/Events/MessageCreatedEventHandler.cs
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace TelegramSearchBot.Domain.Message.Events
{
    /// <summary>
    /// 消息创建事件处理器
    /// </summary>
    public class MessageCreatedEventHandler : INotificationHandler<MessageCreatedEvent>
    {
        private readonly ILogger<MessageCreatedEventHandler> _logger;

        public MessageCreatedEventHandler(ILogger<MessageCreatedEventHandler> logger)
        {
            _logger = logger;
        }

        public async Task Handle(MessageCreatedEvent notification, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Message created: {MessageId} in group {GroupId}", 
                    notification.MessageId, notification.GroupId);

                // 这里可以添加后续处理逻辑，比如：
                // - 索引到搜索引擎
                // - 生成向量嵌入
                // - 发送通知

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling MessageCreatedEvent for message {MessageId}", 
                    notification.MessageId);
                throw;
            }
        }
    }
}
```

### 3.2 实现业务规则

#### 3.2.1 添加业务规则验证

```csharp
// 文件路径：TelegramSearchBot.Domain/Message/Rules/MessageBusinessRules.cs
using System;
using System.Linq;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Domain.Message.Rules
{
    /// <summary>
    /// 消息业务规则验证
    /// </summary>
    public static class MessageBusinessRules
    {
        /// <summary>
        /// 验证消息内容长度
        /// </summary>
        public static bool ValidateContentLength(string content, int maxLength = 4096)
        {
            return !string.IsNullOrEmpty(content) && content.Length <= maxLength;
        }

        /// <summary>
        /// 验证消息ID有效性
        /// </summary>
        public static bool ValidateMessageId(MessageId messageId)
        {
            return messageId != null && messageId.ChatId > 0 && messageId.TelegramMessageId > 0;
        }

        /// <summary>
        /// 验证用户权限
        /// </summary>
        public static bool ValidateUserPermissions(long userId, long groupId)
        {
            // 这里可以实现具体的用户权限验证逻辑
            return userId > 0 && groupId > 0;
        }

        /// <summary>
        /// 验证消息内容合规性
        /// </summary>
        public static bool ValidateContentCompliance(string content)
        {
            if (string.IsNullOrEmpty(content))
                return false;

            // 检查是否包含敏感词
            var sensitiveWords = new[] { "spam", "fake", "scam" };
            return !sensitiveWords.Any(word => content.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }
}
```

### 3.3 性能优化

#### 3.3.1 添加缓存

```csharp
// 文件路径：TelegramSearchBot.Infrastructure/Caching/MessageCacheService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Infrastructure.Caching
{
    /// <summary>
    /// 消息缓存服务
    /// </summary>
    public class MessageCacheService : IMessageRepository
    {
        private readonly IMessageRepository _innerRepository;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        public MessageCacheService(IMessageRepository innerRepository, IMemoryCache cache)
        {
            _innerRepository = innerRepository;
            _cache = cache;
        }

        public async Task<MessageAggregate> GetByIdAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            string cacheKey = $"message_{id.ChatId}_{id.TelegramMessageId}";
            
            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
                return await _innerRepository.GetByIdAsync(id, cancellationToken);
            });
        }

        public async Task<IEnumerable<MessageAggregate>> GetByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            string cacheKey = $"group_messages_{groupId}";
            
            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
                return await _innerRepository.GetByGroupIdAsync(groupId, cancellationToken);
            });
        }

        // 实现其他接口方法...
        public async Task<MessageAggregate> AddAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            var result = await _innerRepository.AddAsync(aggregate, cancellationToken);
            
            // 清除相关缓存
            ClearMessageCache(aggregate.Id.ChatId);
            
            return result;
        }

        public async Task UpdateAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            await _innerRepository.UpdateAsync(aggregate, cancellationToken);
            
            // 清除相关缓存
            ClearMessageCache(aggregate.Id.ChatId);
        }

        public async Task DeleteAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            await _innerRepository.DeleteAsync(id, cancellationToken);
            
            // 清除相关缓存
            ClearMessageCache(id.ChatId);
        }

        // ... 其他方法实现

        private void ClearMessageCache(long groupId)
        {
            string cacheKey = $"group_messages_{groupId}";
            _cache.Remove(cacheKey);
        }
    }
}
```

## 配置和部署

### 更新Program.cs

```csharp
// 文件路径：TelegramSearchBot/Program.cs
using System;
using System.Threading.Tasks;
using TelegramSearchBot.AppBootstrap;
using TelegramSearchBot.Common;

namespace TelegramSearchBot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 初始化日志
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File($"{Env.WorkDir}/logs/log-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                if (args.Length == 0)
                {
                    await GeneralBootstrap.Startup(args);
                }
                else
                {
                    bool success = AppBootstrap.AppBootstrap.TryDispatchStartupByReflection(args);
                    if (!success)
                    {
                        Log.Error("应用程序启动失败。");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "应用程序启动时发生严重错误。");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
```

### 更新GeneralBootstrap

```csharp
// 文件路径：TelegramSearchBot/AppBootstrap/GeneralBootstrap.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;
using TelegramSearchBot.Extension;

namespace TelegramSearchBot.AppBootstrap
{
    public class GeneralBootstrap : AppBootstrap
    {
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices(services => {
                    services.ConfigureUnifiedArchitectureServices();
                });

        public static async Task Startup(string[] args)
        {
            // 检查并创建目录
            Utils.CheckExistsAndCreateDirectorys($"{Env.WorkDir}/logs");
            Directory.SetCurrentDirectory(Env.WorkDir);

            // 配置端口
            Env.SchedulerPort = Utils.GetRandomAvailablePort();
#if DEBUG
            Env.SchedulerPort = 6379;
#endif

            // 启动调度器
            Fork(["Scheduler", $"{Env.SchedulerPort}"]);

            // 创建主机
            IHost host = CreateHostBuilder(args).Build();

            // 数据库迁移
            using (var serviceScope = host.Services.CreateScope())
            {
                var context = serviceScope.ServiceProvider.GetRequiredService<DataDbContext>();
                context.Database.Migrate();
            }

            // 启动主机
            await host.StartAsync();
            Log.Information("Host已启动，定时任务调度器已作为后台服务启动");

            // 保持程序运行
            await host.WaitForShutdownAsync();
        }
    }
}
```

## 总结

这个实施方案提供了：

1. **完整的适配器层**：桥接DDD仓储和现有代码
2. **统一依赖注入配置**：规范服务注册
3. **渐进式迁移路径**：分阶段实施，降低风险
4. **性能优化**：缓存和业务规则验证
5. **领域事件支持**：完整的DDD实现

通过这个方案，可以：
- 解决所有386个编译错误
- 保持现有功能正常运行
- 逐步迁移到DDD架构
- 提高代码质量和可维护性

建议按照阶段逐步实施，每个阶段完成后进行充分测试，确保系统稳定性。
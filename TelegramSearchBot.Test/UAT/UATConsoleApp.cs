using System;
using System.Linq;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Test.UAT
{
    /// <summary>
    /// UAT测试工具类
    /// </summary>
    public static class UATConsoleApp
    {
        public static async Task Main()
        {
            await RunUATTests();
        }

        public static async Task RunUATTests()
        {
            Console.WriteLine("=== TelegramSearchBot UAT 测试开始 ===");
            
            try
            {
                // 创建logger工厂
                using var loggerFactory = LoggerFactory.Create(builder =>
                    builder.AddConsole());
                
                // 创建数据库上下文
                var options = new DbContextOptionsBuilder<DataDbContext>()
                    .UseInMemoryDatabase(databaseName: $"UAT_Console_Test_{Guid.NewGuid()}")
                    .Options;
                
                using var context = new DataDbContext(options);
                var repository = new TelegramSearchBot.Domain.Message.MessageRepository(context, loggerFactory.CreateLogger<TelegramSearchBot.Domain.Message.MessageRepository>());
                var service = new MessageService(repository, loggerFactory.CreateLogger<MessageService>());
                
                Console.WriteLine("✅ 测试环境初始化完成");
                
                // 测试1: 基本消息操作
                await TestBasicMessageOperations(service);
                
                // 测试2: 搜索功能
                await TestSearchFunctionality(service);
                
                // 测试3: 多语言支持
                await TestMultilingualSupport(service);
                
                // 测试4: 性能测试
                await TestPerformance(service);
                
                // 测试5: 特殊字符处理
                await TestSpecialCharacters(service);
                
                Console.WriteLine("🎉 所有UAT测试通过！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ UAT测试失败: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            }
            
            Console.WriteLine("=== TelegramSearchBot UAT 测试结束 ===");
        }
        
        static async Task TestBasicMessageOperations(MessageService service)
        {
            Console.WriteLine("\n=== UAT-01: 基本消息操作测试 ===");
            
            var message = MessageAggregate.Create(
                chatId: 100123456789,
                messageId: 6001,
                content: "控制台UAT测试消息",
                fromUserId: 123456789,
                timestamp: DateTime.UtcNow
            );
            
            await service.AddMessageAsync(message);
            
            var retrieved = await service.GetByIdAsync(6001);
            if (retrieved != null && retrieved.Content.Value == "控制台UAT测试消息")
            {
                Console.WriteLine("✅ 基本消息操作测试通过");
            }
            else
            {
                throw new Exception("基本消息操作测试失败");
            }
        }
        
        static async Task TestSearchFunctionality(MessageService service)
        {
            Console.WriteLine("\n=== UAT-02: 搜索功能测试 ===");
            
            var messages = new[]
            {
                MessageAggregate.Create(100123456789, 6002, "搜索测试消息1", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(100123456789, 6003, "搜索测试消息2", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(100123456789, 6004, "其他内容", 123456789, DateTime.UtcNow)
            };
            
            foreach (var msg in messages)
            {
                await service.AddMessageAsync(msg);
            }
            
            var searchResults = await service.SearchByTextAsync("搜索测试");
            
            if (searchResults != null && searchResults.Count() == 2)
            {
                Console.WriteLine("✅ 搜索功能测试通过");
            }
            else
            {
                throw new Exception("搜索功能测试失败");
            }
        }
        
        static async Task TestMultilingualSupport(MessageService service)
        {
            Console.WriteLine("\n=== UAT-03: 多语言支持测试 ===");
            
            var messages = new[]
            {
                MessageAggregate.Create(100123456789, 6005, "中文测试消息", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(100123456789, 6006, "English test message", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(100123456789, 6007, "日本語テストメッセージ", 123456789, DateTime.UtcNow)
            };
            
            foreach (var msg in messages)
            {
                await service.AddMessageAsync(msg);
            }
            
            var chineseResults = await service.SearchByTextAsync("中文");
            var englishResults = await service.SearchByTextAsync("English");
            var japaneseResults = await service.SearchByTextAsync("日本語");
            
            if (chineseResults.Count() == 1 && englishResults.Count() == 1 && japaneseResults.Count() == 1)
            {
                Console.WriteLine("✅ 多语言支持测试通过");
            }
            else
            {
                throw new Exception("多语言支持测试失败");
            }
        }
        
        static async Task TestPerformance(MessageService service)
        {
            Console.WriteLine("\n=== UAT-04: 性能测试 ===");
            
            var messageCount = 30;
            var messages = Enumerable.Range(1, messageCount)
                .Select(i => MessageAggregate.Create(
                    100123456789,
                    6100 + i,
                    $"性能测试消息 {i}",
                    123456789,
                    DateTime.UtcNow
                ))
                .ToList();
            
            var insertStartTime = DateTime.UtcNow;
            foreach (var msg in messages)
            {
                await service.AddMessageAsync(msg);
            }
            var insertEndTime = DateTime.UtcNow;
            var insertDuration = (insertEndTime - insertStartTime).TotalMilliseconds;
            
            var searchStartTime = DateTime.UtcNow;
            var searchResults = await service.SearchByTextAsync("性能测试");
            var searchEndTime = DateTime.UtcNow;
            var searchDuration = (searchEndTime - searchStartTime).TotalMilliseconds;
            
            if (searchResults.Count() == messageCount && insertDuration < 2000 && searchDuration < 300)
            {
                Console.WriteLine($"✅ 性能测试通过 - 插入: {insertDuration:F2}ms, 搜索: {searchDuration:F2}ms");
            }
            else
            {
                throw new Exception($"性能测试失败 - 找到 {searchResults.Count()} 条消息，期望 {messageCount} 条");
            }
        }
        
        static async Task TestSpecialCharacters(MessageService service)
        {
            Console.WriteLine("\n=== UAT-05: 特殊字符测试 ===");
            
            var messages = new[]
            {
                MessageAggregate.Create(100123456789, 6201, "包含Emoji的消息：🎉😊🚀", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(100123456789, 6202, "包含HTML的消息：<div>测试</div>", 123456789, DateTime.UtcNow),
                MessageAggregate.Create(100123456789, 6203, "包含符号的消息：@#$%^&*()", 123456789, DateTime.UtcNow)
            };
            
            bool allPassed = true;
            foreach (var msg in messages)
            {
                try
                {
                    await service.AddMessageAsync(msg);
                    var retrieved = await service.GetByIdAsync(msg.Id.TelegramMessageId);
                    
                    if (retrieved == null || retrieved.Content.Value != msg.Content.Value)
                    {
                        allPassed = false;
                        break;
                    }
                }
                catch
                {
                    allPassed = false;
                    break;
                }
            }
            
            if (allPassed)
            {
                Console.WriteLine("✅ 特殊字符测试通过");
            }
            else
            {
                throw new Exception("特殊字符测试失败");
            }
        }
    }
}
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Scheduler;
using TelegramSearchBot.Service.Todo;
using Xunit;

namespace TelegramSearchBot.Test.Service.Scheduler {
    public sealed class TodoReminderTaskTests {
        [Fact]
        public async Task ExecuteAsync_SendsDueReminderAndMarksTodoAsSent() {
            var databaseName = $"TodoReminderTaskTests_{Guid.NewGuid():N}";
            var services = new ServiceCollection();
            services.AddDbContext<DataDbContext>(
                options => options.UseInMemoryDatabase(databaseName),
                ServiceLifetime.Transient);
            services.AddTransient<TodoService>();

            var serviceProvider = services.BuildServiceProvider();

            await using (var scope = serviceProvider.CreateAsyncScope()) {
                var todoService = scope.ServiceProvider.GetRequiredService<TodoService>();
                await todoService.CreateTodoAsync(
                    chatId: -100123,
                    userId: 42,
                    sourceMessageId: 10,
                    title: "到点提醒",
                    remindAt: DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
            }

            var botClientMock = new Mock<ITelegramBotClient>();
            botClientMock.SetReturnsDefault(Task.FromResult(new Message {
                Id = 888,
                Date = DateTime.UtcNow,
                Chat = new Chat {
                    Id = -100123,
                    Type = ChatType.Supergroup
                }
            }));

            var sendMessage = new SendMessage(
                botClientMock.Object,
                Mock.Of<ILogger<SendMessage>>());

            var task = new TodoReminderTask(
                serviceProvider,
                botClientMock.Object,
                sendMessage,
                Mock.Of<ILogger<TodoReminderTask>>());

            await task.ExecuteAsync();

            await using var verificationScope = serviceProvider.CreateAsyncScope();
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<DataDbContext>();
            var todo = await dbContext.TodoItems.SingleAsync();
            Assert.NotNull(todo.ReminderSentAtUtc);
            Assert.Equal(888, todo.ReminderMessageId);
        }
    }
}

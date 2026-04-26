using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Todo;
using Xunit;

namespace TelegramSearchBot.Test.Service.Todo {
    public sealed class TodoServiceTests : IDisposable {
        private readonly DataDbContext _dbContext;
        private readonly TodoService _todoService;

        public TodoServiceTests() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase($"TodoServiceTests_{Guid.NewGuid():N}")
                .Options;

            _dbContext = new DataDbContext(options);
            _dbContext.Database.EnsureCreated();
            _todoService = new TodoService(_dbContext);
        }

        [Fact]
        public async Task CreateTodoAsync_CreatesTodoInNamedList() {
            var result = await _todoService.CreateTodoAsync(
                chatId: -100123,
                userId: 42,
                sourceMessageId: 777,
                title: "整理 PR #244",
                listName: "工作",
                description: "补全 todo / reminder / completion flow",
                priority: "high",
                dueAt: "2026-04-25T20:00:00+08:00",
                remindAt: "2026-04-25T18:00:00+08:00");

            Assert.True(result.Success);
            Assert.NotNull(result.Todo);
            Assert.NotNull(result.TodoId);
            Assert.Equal("工作", result.Todo.ListName);
            Assert.Equal(TodoItemStatus.Pending, result.Todo.Status);
            Assert.Equal(1, await _dbContext.TodoLists.CountAsync());
            Assert.Equal(1, await _dbContext.TodoItems.CountAsync());

            var savedTodo = await _dbContext.TodoItems.Include(item => item.TodoList).SingleAsync();
            Assert.Equal("整理 PR #244", savedTodo.Title);
            Assert.Equal("工作", savedTodo.TodoList.Name);
            Assert.NotNull(savedTodo.DueAtUtc);
            Assert.NotNull(savedTodo.RemindAtUtc);
            Assert.Null(savedTodo.ReminderSentAtUtc);
        }

        [Fact]
        public async Task QueryTodosAsync_FiltersPendingItemsByList() {
            await _todoService.CreateTodoAsync(-100123, 42, 1, "完成迁移", "工作", remindAt: "2026-04-25T10:00:00+08:00");
            var completedTodo = await _todoService.CreateTodoAsync(-100123, 42, 2, "已完成事项", "工作");
            await _todoService.CreateTodoAsync(-100123, 42, 3, "买牛奶", "生活");
            await _todoService.CompleteTodoAsync(-100123, completedTodo.TodoId!.Value, 42);

            var result = await _todoService.QueryTodosAsync(-100123, listName: "工作", statusFilter: "pending");

            Assert.Equal(1, result.TotalCount);
            Assert.Single(result.Items);
            Assert.Equal("完成迁移", result.Items[0].Title);
            Assert.Equal("工作", result.Items[0].ListName);
            Assert.Equal(TodoItemStatus.Pending, result.Items[0].Status);
        }

        [Fact]
        public async Task CompleteTodoAsync_MarksTodoCompleted() {
            var created = await _todoService.CreateTodoAsync(-100123, 42, 5, "点击完成按钮");

            var result = await _todoService.CompleteTodoAsync(-100123, created.TodoId!.Value, 1000);

            Assert.True(result.Success);
            Assert.Equal(TodoItemStatus.Completed, result.Status);
            Assert.NotNull(result.CompletedAtUtc);

            var savedTodo = await _dbContext.TodoItems.SingleAsync();
            Assert.Equal(TodoItemStatus.Completed, savedTodo.Status);
            Assert.Equal(1000, savedTodo.CompletedBy);
            Assert.NotNull(savedTodo.CompletedAtUtc);
        }

        [Fact]
        public async Task GetPendingRemindersAsync_ReturnsOnlyDueUnsentPendingTodos() {
            var dueTodo = await _todoService.CreateTodoAsync(-100123, 42, 1, "需要提醒", remindAt: DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"));
            await _todoService.CreateTodoAsync(-100123, 42, 2, "未来提醒", remindAt: DateTimeOffset.UtcNow.AddMinutes(10).ToString("O"));
            var completedTodo = await _todoService.CreateTodoAsync(-100123, 42, 3, "已完成提醒", remindAt: DateTimeOffset.UtcNow.AddMinutes(-3).ToString("O"));
            var sentTodo = await _todoService.CreateTodoAsync(-100123, 42, 4, "已发送提醒", remindAt: DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O"));

            await _todoService.CompleteTodoAsync(-100123, completedTodo.TodoId!.Value, 42);
            await _todoService.MarkReminderSentAsync(sentTodo.TodoId!.Value, 999, DateTime.UtcNow.AddMinutes(-1));

            var result = await _todoService.GetPendingRemindersAsync(DateTime.UtcNow);

            Assert.Single(result);
            Assert.Equal(dueTodo.TodoId, result[0].Id);
            Assert.All(result, todo => Assert.Equal(TodoItemStatus.Pending, todo.Status));
            Assert.All(result, todo => Assert.Null(todo.ReminderSentAtUtc));
        }

        [Fact]
        public async Task UpdateTodoAsync_UpdatesFieldsAndResetsReminderState() {
            var created = await _todoService.CreateTodoAsync(
                chatId: -100123,
                userId: 42,
                sourceMessageId: 10,
                title: "初始标题",
                listName: "工作",
                description: "初始描述",
                priority: "high",
                dueAt: DateTimeOffset.UtcNow.AddDays(1).ToString("O"),
                remindAt: DateTimeOffset.UtcNow.AddHours(1).ToString("O"));

            await _todoService.MarkReminderSentAsync(created.TodoId!.Value, 999, DateTime.UtcNow.AddMinutes(-1));

            var updated = await _todoService.UpdateTodoAsync(
                chatId: -100123,
                todoId: created.TodoId.Value,
                updatedBy: 1000,
                title: "修正后的标题",
                listName: "生活",
                description: "修正后的描述",
                priority: "",
                dueAt: "2026-04-26T21:30:00+08:00",
                remindAt: DateTimeOffset.UtcNow.AddMinutes(30).ToString("O"));

            Assert.True(updated.Success);
            Assert.NotNull(updated.Todo);
            Assert.Equal("修正后的标题", updated.Todo!.Title);
            Assert.Equal("生活", updated.Todo.ListName);
            Assert.Equal("修正后的描述", updated.Todo.Description);
            Assert.Equal(string.Empty, updated.Todo.Priority);
            Assert.Equal("Todo updated and reminder schedule refreshed.", updated.Note);

            var savedTodo = await _dbContext.TodoItems.Include(item => item.TodoList).SingleAsync();
            Assert.Equal("修正后的标题", savedTodo.Title);
            Assert.Equal("生活", savedTodo.TodoList.Name);
            Assert.Equal("修正后的描述", savedTodo.Description);
            Assert.Equal(string.Empty, savedTodo.Priority);
            Assert.NotNull(savedTodo.DueAtUtc);
            Assert.NotNull(savedTodo.RemindAtUtc);
            Assert.Null(savedTodo.ReminderSentAtUtc);
            Assert.Null(savedTodo.ReminderMessageId);
        }

        public void Dispose() {
            _dbContext.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Service.Todo {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class TodoService : IService {
        public const string DefaultListName = "默认";
        private const int MaxPageSize = 50;

        private readonly DataDbContext _dbContext;

        public TodoService(DataDbContext dbContext) {
            _dbContext = dbContext;
        }

        public string ServiceName => "TodoService";

        public async Task<TodoItemResult> CreateTodoAsync(
            long chatId,
            long userId,
            long? sourceMessageId,
            string title,
            string listName = null,
            string description = null,
            string priority = null,
            string dueAt = null,
            string remindAt = null) {
            if (chatId == 0) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    Error = "ChatId is required to create a todo."
                };
            }

            if (string.IsNullOrWhiteSpace(title)) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    Error = "Title is required."
                };
            }

            title = title.Trim();
            if (title.Length > 200) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    Error = "Title must be 200 characters or fewer."
                };
            }

            var normalizedListName = NormalizeListName(listName);
            if (normalizedListName.Length > 100) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    Error = "List name must be 100 characters or fewer."
                };
            }

            description = ( description ?? string.Empty ).Trim();
            if (description.Length > 2000) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    Error = "Description must be 2000 characters or fewer."
                };
            }

            priority = NormalizePriority(priority);
            if (priority.Length > 20) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    Error = "Priority must be 20 characters or fewer."
                };
            }

            if (!TryParseDateTime(dueAt, out var dueAtUtc, out var dueAtError)) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    Error = dueAtError
                };
            }

            if (!TryParseDateTime(remindAt, out var remindAtUtc, out var remindAtError)) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    Error = remindAtError
                };
            }

            if (remindAtUtc.HasValue && dueAtUtc.HasValue && remindAtUtc > dueAtUtc) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    Error = "Reminder time cannot be later than the deadline."
                };
            }

            var todoList = await GetOrCreateTodoListAsync(chatId, normalizedListName, userId);
            var now = DateTime.UtcNow;
            var todo = new TodoItem {
                ChatId = chatId,
                TodoListId = todoList.Id,
                Title = title,
                Description = description,
                Priority = priority,
                Status = TodoItemStatus.Pending,
                CreatedBy = userId,
                SourceMessageId = sourceMessageId,
                CreatedAt = now,
                UpdatedAt = now,
                DueAtUtc = dueAtUtc,
                RemindAtUtc = remindAtUtc
            };

            _dbContext.TodoItems.Add(todo);
            await _dbContext.SaveChangesAsync();

            await _dbContext.Entry(todo).Reference(item => item.TodoList).LoadAsync();

            return new TodoItemResult {
                Success = true,
                ChatId = chatId,
                TodoId = todo.Id,
                Todo = MapTodo(todo),
                Note = remindAtUtc.HasValue
                    ? "Todo created and reminder scheduled."
                    : "Todo created."
            };
        }

        public async Task<TodoQueryResult> QueryTodosAsync(
            long chatId,
            string listName = null,
            string statusFilter = "pending",
            int page = 1,
            int pageSize = 10) {
            if (chatId == 0) {
                return new TodoQueryResult {
                    ChatId = chatId,
                    StatusFilter = statusFilter ?? string.Empty,
                    Note = "ChatId is required to query todos."
                };
            }

            if (page <= 0) {
                page = 1;
            }

            if (pageSize <= 0) {
                pageSize = 10;
            }

            if (pageSize > MaxPageSize) {
                pageSize = MaxPageSize;
            }

            var normalizedStatus = NormalizeStatusFilter(statusFilter);
            if (normalizedStatus == null) {
                return new TodoQueryResult {
                    ChatId = chatId,
                    ListName = NormalizeListNameOrEmpty(listName),
                    StatusFilter = statusFilter ?? string.Empty,
                    CurrentPage = page,
                    PageSize = pageSize,
                    Note = "Invalid status filter. Use pending, completed, or all."
                };
            }

            var normalizedListName = NormalizeListNameOrEmpty(listName);
            var query = _dbContext.TodoItems
                .AsNoTracking()
                .Include(item => item.TodoList)
                .Where(item => item.ChatId == chatId);

            if (!string.IsNullOrWhiteSpace(normalizedListName)) {
                query = query.Where(item => item.TodoList.Name == normalizedListName);
            }

            query = normalizedStatus switch {
                "pending" => query.Where(item => item.Status == TodoItemStatus.Pending),
                "completed" => query.Where(item => item.Status == TodoItemStatus.Completed),
                _ => query
            };

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(item => item.Status == TodoItemStatus.Pending ? 0 : 1)
                .ThenBy(item => item.DueAtUtc ?? DateTime.MaxValue)
                .ThenBy(item => item.RemindAtUtc ?? DateTime.MaxValue)
                .ThenByDescending(item => item.CreatedAt)
                .Skip(( page - 1 ) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new TodoQueryResult {
                ChatId = chatId,
                ListName = normalizedListName,
                StatusFilter = normalizedStatus,
                CurrentPage = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                Items = items.Select(MapTodo).ToList(),
                Note = totalCount == 0 ? "No todo items matched the current filter." : string.Empty
            };
        }

        public async Task<TodoItemResult> UpdateTodoAsync(
            long chatId,
            long todoId,
            long updatedBy,
            string title = null,
            string listName = null,
            string description = null,
            string priority = null,
            string dueAt = null,
            string remindAt = null) {
            if (chatId == 0) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    TodoId = todoId,
                    Error = "ChatId is required to update a todo."
                };
            }

            if (todoId <= 0) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    TodoId = todoId,
                    Error = "todoId must be greater than zero."
                };
            }

            var todo = await _dbContext.TodoItems
                .Include(item => item.TodoList)
                .FirstOrDefaultAsync(item => item.ChatId == chatId && item.Id == todoId);

            if (todo == null) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    TodoId = todoId,
                    Error = "Todo not found in the current chat."
                };
            }

            if (todo.Status == TodoItemStatus.Completed) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    TodoId = todoId,
                    Error = "Completed todos cannot be edited."
                };
            }

            var changed = false;
            var reminderChanged = false;

            if (title != null) {
                if (string.IsNullOrWhiteSpace(title)) {
                    return new TodoItemResult {
                        Success = false,
                        ChatId = chatId,
                        TodoId = todoId,
                        Error = "Title cannot be empty when updating a todo."
                    };
                }

                var normalizedTitle = title.Trim();
                if (normalizedTitle.Length > 200) {
                    return new TodoItemResult {
                        Success = false,
                        ChatId = chatId,
                        TodoId = todoId,
                        Error = "Title must be 200 characters or fewer."
                    };
                }

                if (!string.Equals(todo.Title, normalizedTitle, StringComparison.Ordinal)) {
                    todo.Title = normalizedTitle;
                    changed = true;
                }
            }

            if (listName != null) {
                var normalizedListName = NormalizeListName(listName);
                if (normalizedListName.Length > 100) {
                    return new TodoItemResult {
                        Success = false,
                        ChatId = chatId,
                        TodoId = todoId,
                        Error = "List name must be 100 characters or fewer."
                    };
                }

                if (!string.Equals(todo.TodoList.Name, normalizedListName, StringComparison.Ordinal)) {
                    var todoList = await GetOrCreateTodoListAsync(chatId, normalizedListName, updatedBy);
                    todo.TodoListId = todoList.Id;
                    todo.TodoList = todoList;
                    changed = true;
                }
            }

            if (description != null) {
                var normalizedDescription = description.Trim();
                if (normalizedDescription.Length > 2000) {
                    return new TodoItemResult {
                        Success = false,
                        ChatId = chatId,
                        TodoId = todoId,
                        Error = "Description must be 2000 characters or fewer."
                    };
                }

                if (!string.Equals(todo.Description ?? string.Empty, normalizedDescription, StringComparison.Ordinal)) {
                    todo.Description = normalizedDescription;
                    changed = true;
                }
            }

            if (priority != null) {
                var normalizedPriority = NormalizePriority(priority);
                if (normalizedPriority.Length > 20) {
                    return new TodoItemResult {
                        Success = false,
                        ChatId = chatId,
                        TodoId = todoId,
                        Error = "Priority must be 20 characters or fewer."
                    };
                }

                if (!string.Equals(todo.Priority ?? string.Empty, normalizedPriority, StringComparison.Ordinal)) {
                    todo.Priority = normalizedPriority;
                    changed = true;
                }
            }

            if (!TryResolveDateTimeUpdate(dueAt, todo.DueAtUtc, out var dueChanged, out var resolvedDueAtUtc, out var dueAtError)) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    TodoId = todoId,
                    Error = dueAtError
                };
            }

            if (!TryResolveDateTimeUpdate(remindAt, todo.RemindAtUtc, out var remindChanged, out var resolvedRemindAtUtc, out var remindAtError)) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    TodoId = todoId,
                    Error = remindAtError
                };
            }

            if (resolvedRemindAtUtc.HasValue && resolvedDueAtUtc.HasValue && resolvedRemindAtUtc > resolvedDueAtUtc) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = chatId,
                    TodoId = todoId,
                    Error = "Reminder time cannot be later than the deadline."
                };
            }

            if (dueChanged) {
                todo.DueAtUtc = resolvedDueAtUtc;
                changed = true;
            }

            if (remindChanged) {
                todo.RemindAtUtc = resolvedRemindAtUtc;
                todo.ReminderSentAtUtc = null;
                todo.ReminderMessageId = null;
                changed = true;
                reminderChanged = true;
            }

            if (!changed) {
                return new TodoItemResult {
                    Success = true,
                    ChatId = chatId,
                    TodoId = todo.Id,
                    Todo = MapTodo(todo),
                    Note = "No todo fields changed."
                };
            }

            todo.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return new TodoItemResult {
                Success = true,
                ChatId = chatId,
                TodoId = todo.Id,
                Todo = MapTodo(todo),
                Note = reminderChanged
                    ? "Todo updated and reminder schedule refreshed."
                    : "Todo updated."
            };
        }

        public async Task<TodoCompletionResult> CompleteTodoAsync(long chatId, long todoId, long completedBy) {
            if (chatId == 0) {
                return new TodoCompletionResult {
                    Success = false,
                    ChatId = chatId,
                    TodoId = todoId,
                    Error = "ChatId is required to complete a todo."
                };
            }

            var todo = await _dbContext.TodoItems
                .Include(item => item.TodoList)
                .FirstOrDefaultAsync(item => item.ChatId == chatId && item.Id == todoId);

            if (todo == null) {
                return new TodoCompletionResult {
                    Success = false,
                    ChatId = chatId,
                    TodoId = todoId,
                    Error = "Todo not found in the current chat."
                };
            }

            if (todo.Status == TodoItemStatus.Completed) {
                return new TodoCompletionResult {
                    Success = true,
                    ChatId = chatId,
                    TodoId = todo.Id,
                    Status = todo.Status,
                    CompletedAtUtc = todo.CompletedAtUtc,
                    Todo = MapTodo(todo),
                    Note = "Todo was already completed."
                };
            }

            var now = DateTime.UtcNow;
            todo.Status = TodoItemStatus.Completed;
            todo.CompletedAtUtc = now;
            todo.CompletedBy = completedBy;
            todo.UpdatedAt = now;

            await _dbContext.SaveChangesAsync();

            return new TodoCompletionResult {
                Success = true,
                ChatId = chatId,
                TodoId = todo.Id,
                Status = todo.Status,
                CompletedAtUtc = todo.CompletedAtUtc,
                Todo = MapTodo(todo),
                Note = "Todo completed."
            };
        }

        public async Task<List<TodoItem>> GetPendingRemindersAsync(DateTime utcNow, int maxCount = 50) {
            return await _dbContext.TodoItems
                .Include(item => item.TodoList)
                .Where(item =>
                    item.Status == TodoItemStatus.Pending &&
                    item.RemindAtUtc.HasValue &&
                    item.ReminderSentAtUtc == null &&
                    item.RemindAtUtc <= utcNow)
                .OrderBy(item => item.RemindAtUtc)
                .ThenBy(item => item.Id)
                .Take(maxCount)
                .ToListAsync();
        }

        public async Task MarkReminderSentAsync(long todoId, long reminderMessageId, DateTime sentAtUtc) {
            var todo = await _dbContext.TodoItems.FirstOrDefaultAsync(item => item.Id == todoId);
            if (todo == null) {
                throw new InvalidOperationException($"Todo {todoId} was not found while marking reminder as sent.");
            }

            todo.ReminderSentAtUtc = sentAtUtc;
            todo.ReminderMessageId = reminderMessageId;
            todo.UpdatedAt = sentAtUtc;

            await _dbContext.SaveChangesAsync();
        }

        public string BuildReminderMessage(TodoItem todo) {
            ArgumentNullException.ThrowIfNull(todo);
            ArgumentNullException.ThrowIfNull(todo.TodoList);

            var lines = new List<string> {
                "📋 <b>待办提醒</b>",
                $"🆔 <b>ID:</b> <code>{todo.Id}</code>",
                $"📚 <b>列表:</b> {Encode(todo.TodoList.Name)}",
                $"📌 <b>标题:</b> {Encode(todo.Title)}"
            };

            if (!string.IsNullOrWhiteSpace(todo.Description)) {
                lines.Add($"📝 <b>描述:</b> {Encode(todo.Description)}");
            }

            if (!string.IsNullOrWhiteSpace(todo.Priority)) {
                lines.Add($"🔥 <b>优先级:</b> {Encode(todo.Priority)}");
            }

            if (todo.RemindAtUtc.HasValue) {
                lines.Add($"⏰ <b>提醒时间:</b> {FormatUtcForDisplay(todo.RemindAtUtc.Value)}");
            }

            if (todo.DueAtUtc.HasValue) {
                lines.Add($"⌛ <b>截止时间:</b> {FormatUtcForDisplay(todo.DueAtUtc.Value)}");
            }

            lines.Add("📍 <b>状态:</b> 待完成");

            return string.Join(Environment.NewLine, lines);
        }

        private async Task<TodoList> GetOrCreateTodoListAsync(long chatId, string listName, long userId) {
            var existing = await _dbContext.TodoLists
                .FirstOrDefaultAsync(list => list.ChatId == chatId && list.Name == listName);

            if (existing != null) {
                existing.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return existing;
            }

            var todoList = new TodoList {
                ChatId = chatId,
                Name = listName,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.TodoLists.Add(todoList);
            await _dbContext.SaveChangesAsync();
            return todoList;
        }

        private static TodoToolItem MapTodo(TodoItem todo) {
            return new TodoToolItem {
                TodoId = todo.Id,
                ChatId = todo.ChatId,
                ListName = todo.TodoList?.Name ?? string.Empty,
                Title = todo.Title,
                Description = todo.Description ?? string.Empty,
                Priority = todo.Priority ?? string.Empty,
                Status = todo.Status,
                CreatedAtUtc = todo.CreatedAt,
                DueAtUtc = todo.DueAtUtc,
                RemindAtUtc = todo.RemindAtUtc,
                ReminderSentAtUtc = todo.ReminderSentAtUtc,
                CompletedAtUtc = todo.CompletedAtUtc,
                SourceMessageId = todo.SourceMessageId
            };
        }

        private static string NormalizeListName(string listName) {
            return string.IsNullOrWhiteSpace(listName) ? DefaultListName : listName.Trim();
        }

        private static string NormalizeListNameOrEmpty(string listName) {
            return string.IsNullOrWhiteSpace(listName) ? string.Empty : listName.Trim();
        }

        private static string NormalizePriority(string priority) {
            return string.IsNullOrWhiteSpace(priority) ? string.Empty : priority.Trim();
        }

        private static string? NormalizeStatusFilter(string statusFilter) {
            if (string.IsNullOrWhiteSpace(statusFilter)) {
                return "pending";
            }

            var normalized = statusFilter.Trim().ToLowerInvariant();
            return normalized is "pending" or "completed" or "all" ? normalized : null;
        }

        private static bool TryParseDateTime(string input, out DateTime? utcDateTime, out string error) {
            utcDateTime = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(input)) {
                return true;
            }

            if (!DateTimeOffset.TryParse(
                    input,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                    out var parsed)) {
                error = $"Invalid date/time format: '{input}'. Use a parseable timestamp such as 2026-04-22T18:30:00+08:00.";
                return false;
            }

            utcDateTime = parsed.UtcDateTime;
            return true;
        }

        private static bool TryResolveDateTimeUpdate(
            string input,
            DateTime? currentValue,
            out bool changed,
            out DateTime? resolvedValue,
            out string error) {
            changed = false;
            resolvedValue = currentValue;
            error = string.Empty;

            if (input == null) {
                return true;
            }

            var normalizedInput = input.Trim();
            if (normalizedInput.Length == 0 ||
                normalizedInput.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                normalizedInput.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                normalizedInput.Equals("null", StringComparison.OrdinalIgnoreCase)) {
                changed = currentValue.HasValue;
                resolvedValue = null;
                return true;
            }

            if (!TryParseDateTime(normalizedInput, out var parsedValue, out error)) {
                return false;
            }

            changed = currentValue != parsedValue;
            resolvedValue = parsedValue;
            return true;
        }

        private static string FormatUtcForDisplay(DateTime utcDateTime) {
            return utcDateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        private static string Encode(string value) {
            return HttpUtility.HtmlEncode(value ?? string.Empty);
        }
    }
}

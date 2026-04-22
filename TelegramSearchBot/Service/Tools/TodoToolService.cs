using System.Threading.Tasks;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;
using TelegramSearchBot.Service.Todo;

namespace TelegramSearchBot.Service.Tools {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class TodoToolService : IService {
        private readonly TodoService _todoService;

        public TodoToolService(TodoService todoService) {
            _todoService = todoService;
        }

        public string ServiceName => "TodoToolService";

        [BuiltInTool("Creates a persistent todo item in the current chat. Supports multiple todo lists, optional deadline, and optional reminder time. Prefer explicit ISO 8601 timestamps with timezone offsets.", Name = "create_todo_item")]
        public Task<TodoItemResult> CreateTodoItem(
            [BuiltInParameter("Short todo title. Keep it concise and specific.", IsRequired = true)] string title,
            ToolContext toolContext,
            [BuiltInParameter("Optional todo list name. If omitted, the default list is used.", IsRequired = false)] string listName = null,
            [BuiltInParameter("Optional detailed description or acceptance criteria.", IsRequired = false)] string description = null,
            [BuiltInParameter("Optional priority label such as low, medium, high, or urgent.", IsRequired = false)] string priority = null,
            [BuiltInParameter("Optional due timestamp. Prefer ISO 8601 with timezone, e.g. 2026-04-22T18:30:00+08:00.", IsRequired = false)] string dueAt = null,
            [BuiltInParameter("Optional reminder timestamp. Prefer ISO 8601 with timezone. The bot will remind the chat at this time.", IsRequired = false)] string remindAt = null) {
            var validationError = ValidateToolContext(toolContext);
            if (validationError != null) {
                return Task.FromResult(validationError);
            }

            return _todoService.CreateTodoAsync(
                toolContext.ChatId,
                toolContext.UserId,
                toolContext.MessageId == 0 ? null : toolContext.MessageId,
                title,
                listName,
                description,
                priority,
                dueAt,
                remindAt);
        }

        [BuiltInTool("Queries todo items in the current chat. Use this before completing a todo if you need to discover its todoId. By default, only pending items are returned.", Name = "query_todo_items")]
        public Task<TodoQueryResult> QueryTodoItems(
            ToolContext toolContext,
            [BuiltInParameter("Optional todo list name to filter by.", IsRequired = false)] string listName = null,
            [BuiltInParameter("Status filter: pending, completed, or all. Defaults to pending.", IsRequired = false)] string status = "pending",
            [BuiltInParameter("Page number for pagination. Defaults to 1.", IsRequired = false)] int page = 1,
            [BuiltInParameter("Number of results per page. Defaults to 10 and is capped at 50.", IsRequired = false)] int pageSize = 10) {
            var validationError = ValidateQueryToolContext(toolContext, listName, status, page, pageSize);
            if (validationError != null) {
                return Task.FromResult(validationError);
            }

            return _todoService.QueryTodosAsync(toolContext.ChatId, listName, status, page, pageSize);
        }

        [BuiltInTool("Marks a todo item as completed in the current chat. If the todoId is unknown, query todos first.", Name = "complete_todo_item")]
        public Task<TodoCompletionResult> CompleteTodoItem(
            [BuiltInParameter("The numeric todoId to complete.", IsRequired = true)] long todoId,
            ToolContext toolContext) {
            var validationError = ValidateCompletionToolContext(toolContext, todoId);
            if (validationError != null) {
                return Task.FromResult(validationError);
            }

            return _todoService.CompleteTodoAsync(toolContext.ChatId, todoId, toolContext.UserId);
        }

        private static TodoItemResult ValidateToolContext(ToolContext toolContext) {
            if (toolContext == null || toolContext.ChatId == 0) {
                return new TodoItemResult {
                    Success = false,
                    ChatId = toolContext?.ChatId ?? 0,
                    Error = "ToolContext.ChatId is required."
                };
            }

            return null;
        }

        private static TodoQueryResult ValidateQueryToolContext(ToolContext toolContext, string listName, string status, int page, int pageSize) {
            if (toolContext == null || toolContext.ChatId == 0) {
                return new TodoQueryResult {
                    ChatId = toolContext?.ChatId ?? 0,
                    ListName = listName ?? string.Empty,
                    StatusFilter = status ?? string.Empty,
                    CurrentPage = page,
                    PageSize = pageSize,
                    Note = "ToolContext.ChatId is required."
                };
            }

            return null;
        }

        private static TodoCompletionResult ValidateCompletionToolContext(ToolContext toolContext, long todoId) {
            if (toolContext == null || toolContext.ChatId == 0) {
                return new TodoCompletionResult {
                    Success = false,
                    ChatId = toolContext?.ChatId ?? 0,
                    TodoId = todoId,
                    Error = "ToolContext.ChatId is required."
                };
            }

            if (todoId <= 0) {
                return new TodoCompletionResult {
                    Success = false,
                    ChatId = toolContext.ChatId,
                    TodoId = todoId,
                    Error = "todoId must be greater than zero."
                };
            }

            return null;
        }
    }
}

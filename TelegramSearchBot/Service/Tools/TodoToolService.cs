using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service.Tools
{
    /// <summary>
    /// LLM tool for sending todo messages to group chats
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class TodoToolService
    {
        private readonly ISendMessageService _sendMessageService;
        private readonly ILogger<TodoToolService> _logger;

        public TodoToolService(ISendMessageService sendMessageService, ILogger<TodoToolService> logger)
        {
            _sendMessageService = sendMessageService;
            _logger = logger;
        }

        /// <summary>
        /// Send a todo message to a group chat
        /// </summary>
        /// <param name="chatId">Target group chat ID</param>
        /// <param name="title">Todo title/subject</param>
        /// <param name="description">Detailed description of the todo</param>
        /// <param name="priority">Priority level (low, medium, high, urgent)</param>
        /// <param name="dueDate">Optional due date for the todo</param>
        /// <param name="toolContext">Tool execution context</param>
        /// <returns>Confirmation message</returns>
        [McpTool("Send a todo message to a group chat with structured information including title, description, priority, and optional due date.")]
        public async Task<string> SendTodoToGroup(
            [McpParameter("Target group chat ID to send the todo message to")]
            long chatId,
            
            [McpParameter("Title or subject of the todo item")]
            string title,
            
            [McpParameter("Detailed description of the todo item")]
            string description,
            
            [McpParameter("Priority level (low, medium, high, urgent)", IsRequired = false)]
            string priority = "medium",
            
            [McpParameter("Optional due date for the todo item (format: YYYY-MM-DD or YYYY-MM-DD HH:MM)", IsRequired = false)]
            string dueDate = null,
            
            ToolContext toolContext = null)
        {
            try
            {
                // Validate chat ID is a group chat (negative chat IDs are groups/channels)
                if (chatId > 0)
                {
                    _logger.LogWarning("TodoToolService: Chat ID {ChatId} appears to be a private chat, not a group", chatId);
                }

                // Validate priority
                priority = priority?.ToLowerInvariant() ?? "medium";
                if (!new[] { "low", "medium", "high", "urgent" }.Contains(priority))
                {
                    priority = "medium";
                }

                // Build todo message
                var message = BuildTodoMessage(title, description, priority, dueDate);

                // Send message to group
                await _sendMessageService.SendMessage(message, chatId);

                _logger.LogInformation("TodoToolService: Successfully sent todo to chat {ChatId}: {Title}", chatId, title);

                return $"✅ Todo item '{title}' has been sent to group {chatId} with {priority} priority.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TodoToolService: Failed to send todo to chat {ChatId}: {Title}", chatId, title);
                return $"❌ Failed to send todo item '{title}' to group {chatId}: {ex.Message}";
            }
        }

        /// <summary>
        /// Send a quick todo message with minimal parameters
        /// </summary>
        /// <param name="chatId">Target group chat ID</param>
        /// <param name="message">Todo message content</param>
        /// <param name="toolContext">Tool execution context</param>
        /// <returns>Confirmation message</returns>
        [McpTool("Send a quick todo message to a group chat with minimal formatting")]
        public async Task<string> SendQuickTodo(
            [McpParameter("Target group chat ID to send the todo message to")]
            long chatId,
            
            [McpParameter("Quick todo message content")]
            string message,
            
            ToolContext toolContext = null)
        {
            try
            {
                // Validate chat ID is a group chat (negative chat IDs are groups/channels)
                if (chatId > 0)
                {
                    _logger.LogWarning("TodoToolService: Chat ID {ChatId} appears to be a private chat, not a group", chatId);
                }

                // Build quick todo message
                var formattedMessage = $"📋 **TODO**\n\n{message}";

                // Send message to group
                await _sendMessageService.SendMessage(formattedMessage, chatId);

                _logger.LogInformation("TodoToolService: Successfully sent quick todo to chat {ChatId}", chatId);

                return $"✅ Quick todo has been sent to group {chatId}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TodoToolService: Failed to send quick todo to chat {ChatId}", chatId);
                return $"❌ Failed to send quick todo to group {chatId}: {ex.Message}";
            }
        }

        private string BuildTodoMessage(string title, string description, string priority, string dueDate)
        {
            var priorityEmojis = new Dictionary<string, string>
            {
                { "low", "🟢" },
                { "medium", "🟡" },
                { "high", "🟠" },
                { "urgent", "🔴" }
            };

            var priorityEmoji = priorityEmojis.TryGetValue(priority, out var emoji) ? emoji : "🟡";

            var message = $"📋 **TODO: {title}** {priorityEmoji}\n\n";
            message += $"**Priority:** {priority.ToUpperInvariant()}\n\n";
            message += $"**Description:**\n{description}\n";

            if (!string.IsNullOrEmpty(dueDate))
            {
                message += $"\n**Due Date:** {dueDate}\n";
            }

            message += $"\n*Created: {DateTimeOffset.Now:yyyy-MM-dd HH:mm})*";

            return message;
        }
    }
}
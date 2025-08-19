namespace TelegramSearchBot.Application.Exceptions
{
    /// <summary>
    /// 应用层异常基类
    /// </summary>
    public class ApplicationException : System.Exception
    {
        public string ErrorCode { get; }
        public object Details { get; }

        public ApplicationException(string message, string errorCode = null, object details = null)
            : base(message)
        {
            ErrorCode = errorCode;
            Details = details;
        }

        public ApplicationException(string message, System.Exception innerException, string errorCode = null, object details = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            Details = details;
        }
    }

    /// <summary>
    /// 消息未找到异常
    /// </summary>
    public class MessageNotFoundException : ApplicationException
    {
        public MessageNotFoundException(long messageId) 
            : base($"Message with ID {messageId} not found", "MESSAGE_NOT_FOUND", new { MessageId = messageId })
        {
        }
    }

    /// <summary>
    /// 搜索异常
    /// </summary>
    public class SearchException : ApplicationException
    {
        public SearchException(string query, System.Exception innerException = null) 
            : base($"Search failed for query: {query}", "SEARCH_FAILED", innerException)
        {
        }
    }

    /// <summary>
    /// 验证异常
    /// </summary>
    public class ValidationException : ApplicationException
    {
        public System.Collections.Generic.IEnumerable<string> Errors { get; }

        public ValidationException(string[] errors) 
            : base("Validation failed", "VALIDATION_FAILED", new { Errors = errors })
        {
            Errors = errors;
        }

        public ValidationException(string error) 
            : base("Validation failed", "VALIDATION_FAILED", new { Errors = new[] { error } })
        {
            Errors = new[] { error };
        }
    }
}
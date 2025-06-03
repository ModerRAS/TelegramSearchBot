namespace TelegramSearchBot.LLM.Domain.ValueObjects;

/// <summary>
/// LLM响应值对象
/// </summary>
public record LLMResponse(
    string RequestId,
    string Model,
    bool IsSuccess = true,
    string? Content = null,
    string? ErrorMessage = null,
    bool IsStreaming = false,
    DateTime? StartTime = null,
    DateTime? EndTime = null,
    Dictionary<string, object>? Metadata = null)
{
    public Dictionary<string, object> Metadata { get; init; } = Metadata ?? new Dictionary<string, object>();
    
    /// <summary>
    /// 创建成功响应
    /// </summary>
    public static LLMResponse Success(string requestId, string model, string content, DateTime? startTime = null)
    {
        return new LLMResponse(requestId, model, true, content, null, false, startTime, DateTime.UtcNow);
    }
    
    /// <summary>
    /// 创建失败响应
    /// </summary>
    public static LLMResponse Failure(string requestId, string model, string errorMessage, DateTime? startTime = null)
    {
        return new LLMResponse(requestId, model, false, null, errorMessage, false, startTime, DateTime.UtcNow);
    }
    
    /// <summary>
    /// 创建流式响应
    /// </summary>
    public static LLMResponse Streaming(string requestId, string model, DateTime? startTime = null)
    {
        return new LLMResponse(requestId, model, true, null, null, true, startTime);
    }
} 
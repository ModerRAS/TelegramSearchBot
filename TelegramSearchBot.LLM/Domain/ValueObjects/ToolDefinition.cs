namespace TelegramSearchBot.LLM.Domain.ValueObjects;

/// <summary>
/// 工具参数类型
/// </summary>
public enum ToolParameterType
{
    String,
    Number,
    Boolean,
    Object,
    Array
}

/// <summary>
/// 工具参数定义
/// </summary>
public record ToolParameter(
    string Name,
    ToolParameterType Type,
    string Description,
    bool Required = false,
    object? DefaultValue = null,
    List<string>? EnumValues = null);

/// <summary>
/// 工具定义
/// </summary>
public record ToolDefinition(
    string Name,
    string Description,
    List<ToolParameter> Parameters,
    string? Category = null,
    bool IsEnabled = true);

/// <summary>
/// 工具调用请求
/// </summary>
public record ToolInvocation(
    string ToolName,
    Dictionary<string, object> Parameters,
    string InvocationId = "")
{
    public string InvocationId { get; init; } = InvocationId.IsNullOrEmpty() ? Guid.NewGuid().ToString() : InvocationId;
}

/// <summary>
/// 工具调用结果
/// </summary>
public record ToolInvocationResult(
    string InvocationId,
    string ToolName,
    bool IsSuccess,
    object? Result = null,
    string? ErrorMessage = null,
    DateTime ExecutedAt = default)
{
    public DateTime ExecutedAt { get; init; } = ExecutedAt == default ? DateTime.UtcNow : ExecutedAt;
}

/// <summary>
/// 工具调用上下文
/// </summary>
public record ToolInvocationContext(
    string RequestId,
    List<ToolDefinition> AvailableTools,
    List<ToolInvocation> PendingInvocations = null!,
    List<ToolInvocationResult> CompletedInvocations = null!)
{
    public List<ToolInvocation> PendingInvocations { get; init; } = PendingInvocations ?? new List<ToolInvocation>();
    public List<ToolInvocationResult> CompletedInvocations { get; init; } = CompletedInvocations ?? new List<ToolInvocationResult>();
} 
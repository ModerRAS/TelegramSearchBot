using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.LLM.Domain.Services;

/// <summary>
/// 工具调用服务接口
/// </summary>
public interface IToolInvocationService
{
    /// <summary>
    /// 注册工具
    /// </summary>
    void RegisterTool(ToolDefinition toolDefinition, Func<Dictionary<string, object>, Task<object>> handler);

    /// <summary>
    /// 执行工具调用
    /// </summary>
    Task<ToolInvocationResult> InvokeToolAsync(ToolInvocation invocation, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取可用工具列表
    /// </summary>
    List<ToolDefinition> GetAvailableTools();

    /// <summary>
    /// 检查工具是否存在
    /// </summary>
    bool HasTool(string toolName);
} 
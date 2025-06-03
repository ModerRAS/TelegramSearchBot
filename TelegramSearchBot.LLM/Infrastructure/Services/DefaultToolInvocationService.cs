using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.LLM.Infrastructure.Services;

/// <summary>
/// 默认工具调用服务实现
/// </summary>
public class DefaultToolInvocationService : IToolInvocationService
{
    private readonly Dictionary<string, (ToolDefinition Definition, Func<Dictionary<string, object>, Task<object>> Handler)> _tools;
    private readonly ILogger<DefaultToolInvocationService> _logger;

    public DefaultToolInvocationService(ILogger<DefaultToolInvocationService> logger)
    {
        _tools = new Dictionary<string, (ToolDefinition, Func<Dictionary<string, object>, Task<object>>)>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterTool(ToolDefinition toolDefinition, Func<Dictionary<string, object>, Task<object>> handler)
    {
        ArgumentNullException.ThrowIfNull(toolDefinition);
        ArgumentNullException.ThrowIfNull(handler);

        if (_tools.ContainsKey(toolDefinition.Name))
        {
            _logger.LogWarning("替换已存在的工具: {ToolName}", toolDefinition.Name);
        }

        _tools[toolDefinition.Name] = (toolDefinition, handler);
        _logger.LogInformation("注册工具: {ToolName}, Category: {Category}", 
            toolDefinition.Name, toolDefinition.Category ?? "Default");
    }

    public async Task<ToolInvocationResult> InvokeToolAsync(
        ToolInvocation invocation, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        if (!_tools.TryGetValue(invocation.ToolName, out var tool))
        {
            var errorMessage = $"工具不存在: {invocation.ToolName}";
            _logger.LogError(errorMessage);

            return new ToolInvocationResult(
                invocation.InvocationId,
                invocation.ToolName,
                false,
                ErrorMessage: errorMessage);
        }

        var (definition, handler) = tool;

        if (!definition.IsEnabled)
        {
            var errorMessage = $"工具已禁用: {invocation.ToolName}";
            _logger.LogWarning(errorMessage);

            return new ToolInvocationResult(
                invocation.InvocationId,
                invocation.ToolName,
                false,
                ErrorMessage: errorMessage);
        }

        try
        {
            _logger.LogInformation("开始执行工具调用: {ToolName}, InvocationId: {InvocationId}", 
                invocation.ToolName, invocation.InvocationId);

            // 验证参数
            var validationResult = ValidateParameters(definition, invocation.Parameters);
            if (!validationResult.IsValid)
            {
                _logger.LogError("工具参数验证失败: {ToolName}, Error: {Error}", 
                    invocation.ToolName, validationResult.ErrorMessage);

                return new ToolInvocationResult(
                    invocation.InvocationId,
                    invocation.ToolName,
                    false,
                    ErrorMessage: validationResult.ErrorMessage);
            }

            // 执行工具
            var result = await handler(invocation.Parameters);
            
            _logger.LogInformation("工具调用执行完成: {ToolName}, InvocationId: {InvocationId}", 
                invocation.ToolName, invocation.InvocationId);

            return new ToolInvocationResult(
                invocation.InvocationId,
                invocation.ToolName,
                true,
                Result: result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "工具调用执行异常: {ToolName}, InvocationId: {InvocationId}", 
                invocation.ToolName, invocation.InvocationId);

            return new ToolInvocationResult(
                invocation.InvocationId,
                invocation.ToolName,
                false,
                ErrorMessage: ex.Message);
        }
    }

    public List<ToolDefinition> GetAvailableTools()
    {
        return _tools.Values
            .Where(t => t.Definition.IsEnabled)
            .Select(t => t.Definition)
            .ToList();
    }

    public bool HasTool(string toolName)
    {
        return !string.IsNullOrEmpty(toolName) && _tools.ContainsKey(toolName);
    }

    private (bool IsValid, string? ErrorMessage) ValidateParameters(
        ToolDefinition definition, 
        Dictionary<string, object> parameters)
    {
        // 检查必需参数
        var requiredParams = definition.Parameters.Where(p => p.Required).ToList();
        foreach (var requiredParam in requiredParams)
        {
            if (!parameters.ContainsKey(requiredParam.Name))
            {
                return (false, $"缺少必需参数: {requiredParam.Name}");
            }
        }

        // 检查参数类型（简单验证）
        foreach (var param in definition.Parameters)
        {
            if (parameters.TryGetValue(param.Name, out var value) && value != null)
            {
                if (!IsValidParameterType(value, param.Type))
                {
                    return (false, $"参数 {param.Name} 类型不匹配，期望: {param.Type}");
                }
            }
        }

        return (true, null);
    }

    private static bool IsValidParameterType(object value, ToolParameterType expectedType)
    {
        return expectedType switch
        {
            ToolParameterType.String => value is string,
            ToolParameterType.Number => value is int or long or float or double or decimal,
            ToolParameterType.Boolean => value is bool,
            ToolParameterType.Object => value is Dictionary<string, object> or object,
            ToolParameterType.Array => value is Array or List<object>,
            _ => true // 默认允许
        };
    }
} 
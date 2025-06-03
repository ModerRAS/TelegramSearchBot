using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.LLM.Infrastructure.Decorators;

/// <summary>
/// 工具调用装饰器 - 为LLM服务添加工具调用功能
/// </summary>
public class ToolInvocationLLMServiceDecorator : LLMServiceDecoratorBase
{
    private readonly IToolInvocationService _toolInvocationService;
    private readonly ILogger<ToolInvocationLLMServiceDecorator> _logger;
    private readonly int _maxToolInvocations;

    // 工具调用识别正则表达式
    private static readonly Regex ToolCallRegex = new(
        @"<tool_call>\s*(?<json>\{.*?\})\s*</tool_call>", 
        RegexOptions.Compiled | RegexOptions.Singleline);

    public ToolInvocationLLMServiceDecorator(
        ILLMService innerService, 
        IToolInvocationService toolInvocationService,
        ILogger<ToolInvocationLLMServiceDecorator> logger,
        int maxToolInvocations = 5) 
        : base(innerService)
    {
        _toolInvocationService = toolInvocationService ?? throw new ArgumentNullException(nameof(toolInvocationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxToolInvocations = maxToolInvocations;
    }

    public override async Task<LLMResponse> ExecuteAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        var enhancedRequest = EnhanceRequestWithToolPrompt(request);
        var response = await base.ExecuteAsync(enhancedRequest, cancellationToken);

        if (!response.IsSuccess || string.IsNullOrEmpty(response.Content))
        {
            return response;
        }

        // 检查是否包含工具调用
        var toolCalls = ExtractToolCalls(response.Content);
        if (!toolCalls.Any())
        {
            return response;
        }

        _logger.LogInformation("检测到工具调用请求: {Count}个工具, RequestId: {RequestId}", 
            toolCalls.Count, request.RequestId);

        // 执行工具调用
        var toolResults = await ExecuteToolCallsAsync(toolCalls, cancellationToken);
        
        // 构建包含工具结果的新请求
        var newChatHistory = new List<LLMMessage>(request.ChatHistory)
        {
            new(LLMRole.Assistant, response.Content),
            new(LLMRole.User, FormatToolResults(toolResults))
        };

        var newRequest = request with { ChatHistory = newChatHistory };
        
        // 递归执行，以防需要进一步的工具调用
        return await ExecuteWithToolInvocationLimit(newRequest, 1, cancellationToken);
    }

    public override async Task<(ChannelReader<string> StreamReader, Task<LLMResponse> ResponseTask)> ExecuteStreamAsync(
        LLMRequest request, 
        CancellationToken cancellationToken = default)
    {
        var enhancedRequest = EnhanceRequestWithToolPrompt(request);
        var (originalStreamReader, originalResponseTask) = await base.ExecuteStreamAsync(enhancedRequest, cancellationToken);

        // 创建新的流处理器来处理工具调用
        var channel = Channel.CreateUnbounded<string>();
        var processedResponseTask = ProcessStreamWithToolInvocation(
            request, originalStreamReader, originalResponseTask, channel.Writer, cancellationToken);

        return (channel.Reader, processedResponseTask);
    }

    private LLMRequest EnhanceRequestWithToolPrompt(LLMRequest request)
    {
        var availableTools = _toolInvocationService.GetAvailableTools();
        if (!availableTools.Any())
        {
            return request; // 无工具可用，不修改请求
        }

        var toolPrompt = BuildToolPrompt(availableTools);
        var enhancedSystemPrompt = string.IsNullOrEmpty(request.SystemPrompt)
            ? toolPrompt
            : $"{request.SystemPrompt}\n\n{toolPrompt}";

        return request with { SystemPrompt = enhancedSystemPrompt };
    }

    private string BuildToolPrompt(List<ToolDefinition> tools)
    {
        var toolDescriptions = tools.Select(tool =>
        {
            var parameters = string.Join(", ", tool.Parameters.Select(p => 
                $"{p.Name}: {p.Type}" + (p.Required ? " (必需)" : " (可选)")));
            
            return $"- {tool.Name}: {tool.Description}\n  参数: {parameters}";
        });

        return $@"你可以使用以下工具来帮助回答问题：

{string.Join("\n", toolDescriptions)}

当你需要使用工具时，请使用以下格式：
<tool_call>
{{
    ""tool_name"": ""工具名称"",
    ""parameters"": {{
        ""参数名"": ""参数值""
    }}
}}
</tool_call>

你可以在一次回复中调用多个工具。工具调用完成后，我会将结果返回给你，然后你可以基于这些结果继续回答用户的问题。";
    }

    private List<ToolInvocation> ExtractToolCalls(string content)
    {
        var toolCalls = new List<ToolInvocation>();
        var matches = ToolCallRegex.Matches(content);

        foreach (Match match in matches)
        {
            try
            {
                var jsonText = match.Groups["json"].Value;
                var toolCallData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonText);

                if (toolCallData?.TryGetValue("tool_name", out var toolNameObj) == true &&
                    toolNameObj?.ToString() is string toolName &&
                    !string.IsNullOrEmpty(toolName))
                {
                    var parameters = new Dictionary<string, object>();
                    
                    if (toolCallData.TryGetValue("parameters", out var parametersObj) &&
                        parametersObj is JsonElement parametersElement)
                    {
                        parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(parametersElement.GetRawText()) 
                                   ?? new Dictionary<string, object>();
                    }

                    toolCalls.Add(new ToolInvocation(toolName, parameters));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析工具调用失败: {ToolCallJson}", match.Groups["json"].Value);
            }
        }

        return toolCalls;
    }

    private async Task<List<ToolInvocationResult>> ExecuteToolCallsAsync(
        List<ToolInvocation> toolCalls, 
        CancellationToken cancellationToken)
    {
        var results = new List<ToolInvocationResult>();

        foreach (var toolCall in toolCalls)
        {
            try
            {
                var result = await _toolInvocationService.InvokeToolAsync(toolCall, cancellationToken);
                results.Add(result);
                
                _logger.LogInformation("工具调用完成: {ToolName}, Success: {Success}, InvocationId: {InvocationId}", 
                    toolCall.ToolName, result.IsSuccess, result.InvocationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "工具调用异常: {ToolName}", toolCall.ToolName);
                
                results.Add(new ToolInvocationResult(
                    toolCall.InvocationId,
                    toolCall.ToolName,
                    false,
                    ErrorMessage: ex.Message));
            }
        }

        return results;
    }

    private string FormatToolResults(List<ToolInvocationResult> results)
    {
        var formattedResults = results.Select(result =>
        {
            if (result.IsSuccess)
            {
                var resultText = result.Result?.ToString() ?? "无返回值";
                return $"工具 {result.ToolName} 执行成功:\n{resultText}";
            }
            else
            {
                return $"工具 {result.ToolName} 执行失败: {result.ErrorMessage}";
            }
        });

        return $"工具调用结果:\n{string.Join("\n\n", formattedResults)}\n\n请基于以上工具调用结果继续回答用户的问题。";
    }

    private async Task<LLMResponse> ExecuteWithToolInvocationLimit(
        LLMRequest request, 
        int invocationCount, 
        CancellationToken cancellationToken)
    {
        if (invocationCount >= _maxToolInvocations)
        {
            _logger.LogWarning("已达到最大工具调用次数限制: {MaxInvocations}, RequestId: {RequestId}", 
                _maxToolInvocations, request.RequestId);
            
            return LLMResponse.Failure(
                request.RequestId, 
                request.Model, 
                "已达到最大工具调用次数限制", 
                request.StartTime);
        }

        var response = await base.ExecuteAsync(request, cancellationToken);
        
        if (!response.IsSuccess || string.IsNullOrEmpty(response.Content))
        {
            return response;
        }

        var toolCalls = ExtractToolCalls(response.Content);
        if (!toolCalls.Any())
        {
            return response; // 没有更多工具调用，返回最终结果
        }

        // 继续执行工具调用
        var toolResults = await ExecuteToolCallsAsync(toolCalls, cancellationToken);
        
        var newChatHistory = new List<LLMMessage>(request.ChatHistory)
        {
            new(LLMRole.Assistant, response.Content),
            new(LLMRole.User, FormatToolResults(toolResults))
        };

        var newRequest = request with { ChatHistory = newChatHistory };
        
        return await ExecuteWithToolInvocationLimit(newRequest, invocationCount + 1, cancellationToken);
    }

    private async Task<LLMResponse> ProcessStreamWithToolInvocation(
        LLMRequest originalRequest,
        ChannelReader<string> originalStreamReader,
        Task<LLMResponse> originalResponseTask,
        ChannelWriter<string> outputWriter,
        CancellationToken cancellationToken)
    {
        try
        {
            var contentBuilder = new System.Text.StringBuilder();
            
            // 转发原始流内容
            await foreach (var chunk in originalStreamReader.ReadAllAsync(cancellationToken))
            {
                contentBuilder.Append(chunk);
                await outputWriter.WriteAsync(chunk, cancellationToken);
            }

            var originalResponse = await originalResponseTask;
            
            if (!originalResponse.IsSuccess)
            {
                return originalResponse;
            }

            var fullContent = contentBuilder.ToString();
            var toolCalls = ExtractToolCalls(fullContent);
            
            if (!toolCalls.Any())
            {
                return originalResponse; // 没有工具调用，直接返回
            }

            // 执行工具调用并继续流式处理
            await outputWriter.WriteAsync("\n\n[执行工具调用...]\n", cancellationToken);
            
            var toolResults = await ExecuteToolCallsAsync(toolCalls, cancellationToken);
            
            // 构建新请求并继续流式处理
            var newChatHistory = new List<LLMMessage>(originalRequest.ChatHistory)
            {
                new(LLMRole.Assistant, fullContent),
                new(LLMRole.User, FormatToolResults(toolResults))
            };

            var newRequest = originalRequest with { ChatHistory = newChatHistory };
            var (newStreamReader, newResponseTask) = await base.ExecuteStreamAsync(newRequest, cancellationToken);
            
            // 转发新的流内容
            await foreach (var chunk in newStreamReader.ReadAllAsync(cancellationToken))
            {
                await outputWriter.WriteAsync(chunk, cancellationToken);
            }

            return await newResponseTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "流式工具调用处理异常: RequestId: {RequestId}", originalRequest.RequestId);
            throw;
        }
        finally
        {
            outputWriter.TryComplete();
        }
    }
} 
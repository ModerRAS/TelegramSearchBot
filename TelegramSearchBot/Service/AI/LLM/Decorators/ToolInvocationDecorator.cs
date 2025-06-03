using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model;
using TelegramSearchBot.Attributes;

namespace TelegramSearchBot.Service.AI.LLM.Decorators
{
    /// <summary>
    /// 工具调用装饰器 - 集成工具调用功能
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)]
    public class ToolInvocationDecorator : BaseLLMDecorator
    {
        private readonly ILogger<ToolInvocationDecorator> _logger;
        private readonly int _maxToolCycles;
        private readonly string _botName;

        public ToolInvocationDecorator(
            ILLMStreamService innerService,
            ILogger<ToolInvocationDecorator> logger,
            int maxToolCycles = 5,
            string botName = "AI Assistant") : base(innerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxToolCycles = maxToolCycles;
            _botName = botName ?? "AI Assistant";
        }

        public override async IAsyncEnumerable<string> ExecAsync(
            Message message, 
            long chatId, 
            string modelName, 
            LLMChannel channel,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string nextMessageToSend = message.Content;
            var currentLlmResponseBuilder = new StringBuilder();
            
            for (int cycle = 0; cycle < _maxToolCycles; cycle++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new TaskCanceledException();
                }

                // 重置响应构建器
                currentLlmResponseBuilder.Clear();
                bool receivedAnyToken = false;
                
                _logger.LogDebug("工具调用循环 {Cycle}: 发送消息 {Message}", cycle + 1, nextMessageToSend);

                // 创建临时消息对象
                var tempMessage = new Message { Content = nextMessageToSend };
                
                // 获取LLM响应流
                await foreach (var token in _innerService.ExecAsync(tempMessage, chatId, modelName, channel, cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }
                    
                    currentLlmResponseBuilder.Append(token);
                    receivedAnyToken = true;
                    
                    // 对于第一轮（用户原始请求），实时流式返回
                    if (cycle == 0)
                    {
                        yield return token;
                    }
                }

                string llmFullResponseText = currentLlmResponseBuilder.ToString().Trim();
                _logger.LogDebug("LLM完整响应 (循环 {Cycle}): {Response}", cycle + 1, llmFullResponseText);

                if (!receivedAnyToken && cycle < _maxToolCycles - 1 && !string.IsNullOrEmpty(nextMessageToSend))
                {
                    _logger.LogWarning("LLM在工具循环 {Cycle} 中返回空流，输入为: '{Input}'", cycle + 1, nextMessageToSend);
                }

                // 解析工具调用
                var cleanedResponse = McpToolHelper.CleanLlmResponse(llmFullResponseText);
                
                if (McpToolHelper.TryParseToolCalls(cleanedResponse, out var parsedToolCalls) && parsedToolCalls.Any())
                {
                    var firstToolCall = parsedToolCalls[0];
                    string parsedToolName = firstToolCall.toolName;
                    Dictionary<string, string> toolArguments = firstToolCall.arguments;

                    _logger.LogInformation("LLM请求工具调用: {ToolName}，参数: {Arguments}", 
                        parsedToolName, JsonConvert.SerializeObject(toolArguments));
                        
                    if (parsedToolCalls.Count > 1)
                    {
                        _logger.LogWarning("LLM返回了多个工具调用 ({Count})，只执行第一个: '{FirstToolName}'", 
                            parsedToolCalls.Count, parsedToolName);
                    }

                    // 执行工具
                    string toolResultString;
                    bool isError = false;
                    try
                    {
                        var toolContext = new ToolContext { ChatId = chatId };
                        object toolResultObject = await McpToolHelper.ExecuteRegisteredToolAsync(
                            parsedToolName, toolArguments, toolContext);
                        toolResultString = McpToolHelper.ConvertToolResultToString(toolResultObject);
                        
                        _logger.LogInformation("工具 {ToolName} 执行成功，结果: {Result}", 
                            parsedToolName, toolResultString);
                    }
                    catch (Exception ex)
                    {
                        isError = true;
                        _logger.LogError(ex, "执行工具 {ToolName} 时发生错误", parsedToolName);
                        toolResultString = $"执行工具 {parsedToolName} 时发生错误: {ex.Message}";
                    }

                    // 准备下一轮消息
                    string feedbackPrefix = isError 
                        ? $"[工具 '{parsedToolName}' 执行失败。错误: " 
                        : $"[已执行工具 '{parsedToolName}'。结果: ";
                    
                    nextMessageToSend = $"{feedbackPrefix}{toolResultString}]";
                    _logger.LogInformation("为下次LLM调用准备反馈: {Feedback}", nextMessageToSend);
                    
                    // 继续循环进行下一轮工具调用
                }
                else
                {
                    // 不是工具调用，返回最终响应
                    if (string.IsNullOrWhiteSpace(llmFullResponseText) && receivedAnyToken)
                    {
                        _logger.LogWarning("LLM返回了空的最终非工具响应，聊天ID: {ChatId}", chatId);
                    }
                    else if (!receivedAnyToken && string.IsNullOrEmpty(llmFullResponseText))
                    {
                        _logger.LogWarning("LLM返回了空流和空的最终非工具响应，聊天ID: {ChatId}", chatId);
                    }

                    // 如果不是第一轮，需要返回清理后的响应
                    if (cycle > 0)
                    {
                        yield return cleanedResponse;
                    }
                    
                    yield break; // 结束工具调用循环
                }
            }

            // 达到最大工具调用次数
            _logger.LogWarning("达到最大工具调用循环次数 {MaxCycles}，聊天ID: {ChatId}", _maxToolCycles, chatId);
            yield return "我似乎陷入了工具调用循环。请重新表述您的请求或检查工具定义。";
        }

        /// <summary>
        /// 格式化系统提示词，包含工具描述
        /// </summary>
        public string FormatSystemPrompt(long chatId)
        {
            return McpToolHelper.FormatSystemPrompt(_botName, chatId);
        }

        /// <summary>
        /// 清理LLM响应
        /// </summary>
        public static string CleanResponse(string rawResponse)
        {
            return McpToolHelper.CleanLlmResponse(rawResponse);
        }

        /// <summary>
        /// 解析工具调用
        /// </summary>
        public static bool TryParseToolCalls(string input, out List<(string toolName, Dictionary<string, string> arguments)> parsedToolCalls)
        {
            return McpToolHelper.TryParseToolCalls(input, out parsedToolCalls);
        }

        /// <summary>
        /// 执行工具
        /// </summary>
        public static async Task<object> ExecuteToolAsync(string toolName, Dictionary<string, string> arguments, ToolContext context = null)
        {
            return await McpToolHelper.ExecuteRegisteredToolAsync(toolName, arguments, context);
        }

        /// <summary>
        /// 将工具结果转换为字符串
        /// </summary>
        public static string ConvertToolResultToString(object toolResult)
        {
            return McpToolHelper.ConvertToolResultToString(toolResult);
        }
    }
} 
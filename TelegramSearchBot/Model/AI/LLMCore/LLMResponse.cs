using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Model.AI
{
    /// <summary>
    /// LLM响应模型
    /// </summary>
    public class LLMResponse
    {
        /// <summary>
        /// 请求ID
        /// </summary>
        public string RequestId { get; set; }

        /// <summary>
        /// 模型名称
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 响应内容
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 警告消息
        /// </summary>
        public string WarningMessage { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 处理耗时
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>
        /// 使用的token数量
        /// </summary>
        public LLMTokenUsage TokenUsage { get; set; } = new LLMTokenUsage();

        /// <summary>
        /// 工具调用列表
        /// </summary>
        public List<LLMToolCall> ToolCalls { get; set; } = new List<LLMToolCall>();

        /// <summary>
        /// 流式响应标记
        /// </summary>
        public bool IsStreaming { get; set; }

        /// <summary>
        /// 响应元数据
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Token使用统计
    /// </summary>
    public class LLMTokenUsage
    {
        /// <summary>
        /// 输入token数
        /// </summary>
        public int PromptTokens { get; set; }

        /// <summary>
        /// 输出token数
        /// </summary>
        public int CompletionTokens { get; set; }

        /// <summary>
        /// 总token数
        /// </summary>
        public int TotalTokens => PromptTokens + CompletionTokens;

        /// <summary>
        /// 估算成本（美元）
        /// </summary>
        public decimal EstimatedCost { get; set; }
    }

    /// <summary>
    /// LLM工具调用模型
    /// </summary>
    public class LLMToolCall
    {
        /// <summary>
        /// 工具调用ID
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 工具名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 工具参数
        /// </summary>
        public Dictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 工具执行结果
        /// </summary>
        public string Result { get; set; }

        /// <summary>
        /// 执行状态
        /// </summary>
        public LLMToolCallStatus Status { get; set; } = LLMToolCallStatus.Pending;

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 执行开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 执行结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 执行耗时
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;
    }

    /// <summary>
    /// LLM工具定义模型
    /// </summary>
    public class LLMTool
    {
        /// <summary>
        /// 工具名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 工具描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 工具参数定义
        /// </summary>
        public List<LLMToolParameter> Parameters { get; set; } = new List<LLMToolParameter>();

        /// <summary>
        /// 工具类别
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// 是否为危险操作
        /// </summary>
        public bool IsDangerous { get; set; }

        /// <summary>
        /// 权限要求
        /// </summary>
        public List<string> RequiredPermissions { get; set; } = new List<string>();

        /// <summary>
        /// 工具元数据
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// LLM工具参数定义
    /// </summary>
    public class LLMToolParameter
    {
        /// <summary>
        /// 参数名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 参数类型
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 参数描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否必需
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// 默认值
        /// </summary>
        public object DefaultValue { get; set; }

        /// <summary>
        /// 枚举值（如果适用）
        /// </summary>
        public List<object> EnumValues { get; set; }

        /// <summary>
        /// 验证规则
        /// </summary>
        public Dictionary<string, object> ValidationRules { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 图像分析请求模型
    /// </summary>
    public class LLMImageAnalysisRequest
    {
        /// <summary>
        /// 请求ID
        /// </summary>
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// LLM提供商
        /// </summary>
        public LLMProvider Provider { get; set; }

        /// <summary>
        /// 模型名称
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// 渠道配置DTO
        /// </summary>
        public LLMChannelDto Channel { get; set; }

        /// <summary>
        /// 图像内容
        /// </summary>
        public LLMImageContent Image { get; set; }

        /// <summary>
        /// 分析提示词
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// 分析类型
        /// </summary>
        public LLMImageAnalysisType AnalysisType { get; set; } = LLMImageAnalysisType.General;

        /// <summary>
        /// 生成选项
        /// </summary>
        public LLMGenerationOptions Options { get; set; } = new LLMGenerationOptions();

        /// <summary>
        /// 上下文信息
        /// </summary>
        public LLMContext Context { get; set; } = new LLMContext();
    }

    /// <summary>
    /// 聊天历史构建器 - 用于手动构建对话历史
    /// </summary>
    public class LLMChatHistoryBuilder
    {
        private readonly List<LLMMessage> _messages = new List<LLMMessage>();

        /// <summary>
        /// 添加系统消息
        /// </summary>
        public LLMChatHistoryBuilder AddSystemMessage(string content)
        {
            _messages.Add(new LLMMessage
            {
                Role = LLMRole.System,
                Content = content
            });
            return this;
        }

        /// <summary>
        /// 添加用户消息
        /// </summary>
        public LLMChatHistoryBuilder AddUserMessage(string content)
        {
            _messages.Add(new LLMMessage
            {
                Role = LLMRole.User,
                Content = content
            });
            return this;
        }

        /// <summary>
        /// 添加助手消息
        /// </summary>
        public LLMChatHistoryBuilder AddAssistantMessage(string content)
        {
            _messages.Add(new LLMMessage
            {
                Role = LLMRole.Assistant,
                Content = content
            });
            return this;
        }

        /// <summary>
        /// 添加工具结果消息
        /// </summary>
        public LLMChatHistoryBuilder AddToolMessage(string content, string toolCallId = null)
        {
            _messages.Add(new LLMMessage
            {
                Role = LLMRole.Tool,
                Content = content,
                ToolCallId = toolCallId
            });
            return this;
        }

        /// <summary>
        /// 添加多模态用户消息
        /// </summary>
        public LLMChatHistoryBuilder AddUserMessage(string text, LLMImageContent image)
        {
            var message = new LLMMessage
            {
                Role = LLMRole.User,
                Content = text
            };

            if (image != null)
            {
                message.Contents.Add(new LLMContent
                {
                    Type = LLMContentType.Image,
                    Image = image
                });
            }

            _messages.Add(message);
            return this;
        }

        /// <summary>
        /// 添加多模态用户消息（音频）
        /// </summary>
        public LLMChatHistoryBuilder AddUserMessage(string text, LLMAudioContent audio)
        {
            var message = new LLMMessage
            {
                Role = LLMRole.User,
                Content = text
            };

            if (audio != null)
            {
                message.Contents.Add(new LLMContent
                {
                    Type = LLMContentType.Audio,
                    Audio = audio
                });
            }

            _messages.Add(message);
            return this;
        }

        /// <summary>
        /// 添加文件消息
        /// </summary>
        public LLMChatHistoryBuilder AddUserMessage(string text, LLMFileContent file)
        {
            var message = new LLMMessage
            {
                Role = LLMRole.User,
                Content = text
            };

            if (file != null)
            {
                message.Contents.Add(new LLMContent
                {
                    Type = LLMContentType.File,
                    File = file
                });
            }

            _messages.Add(message);
            return this;
        }

        /// <summary>
        /// 添加自定义消息
        /// </summary>
        public LLMChatHistoryBuilder AddMessage(LLMMessage message)
        {
            if (message != null)
                _messages.Add(message);
            return this;
        }

        /// <summary>
        /// 限制历史消息数量
        /// </summary>
        public LLMChatHistoryBuilder Limit(int count)
        {
            if (count > 0 && _messages.Count > count)
            {
                var toRemove = _messages.Count - count;
                _messages.RemoveRange(0, toRemove);
            }
            return this;
        }

        /// <summary>
        /// 限制历史消息的token数量（估算）
        /// </summary>
        public LLMChatHistoryBuilder LimitByTokens(int maxTokens)
        {
            // 简单估算：平均4字符=1token
            int currentTokens = 0;
            var validMessages = new List<LLMMessage>();

            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                var message = _messages[i];
                int messageTokens = (message.Content?.Length ?? 0) / 4;
                
                if (currentTokens + messageTokens <= maxTokens)
                {
                    validMessages.Insert(0, message);
                    currentTokens += messageTokens;
                }
                else
                {
                    break;
                }
            }

            _messages.Clear();
            _messages.AddRange(validMessages);
            return this;
        }

        /// <summary>
        /// 构建聊天历史
        /// </summary>
        public List<LLMMessage> Build()
        {
            return new List<LLMMessage>(_messages);
        }

        /// <summary>
        /// 清空历史
        /// </summary>
        public LLMChatHistoryBuilder Clear()
        {
            _messages.Clear();
            return this;
        }
    }
} 
using System;
using System.Collections.Generic;
using System.IO;

namespace TelegramSearchBot.Model.AI
{
    /// <summary>
    /// LLM请求模型 - 封装各种输入类型
    /// </summary>
    public class LLMRequest
    {
        /// <summary>
        /// 请求唯一标识符
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
        /// 聊天历史记录
        /// </summary>
        public List<LLMMessage> ChatHistory { get; set; } = new List<LLMMessage>();

        /// <summary>
        /// 当前用户消息
        /// </summary>
        public LLMMessage CurrentMessage { get; set; }

        /// <summary>
        /// 系统提示词
        /// </summary>
        public string SystemPrompt { get; set; }

        /// <summary>
        /// 生成参数
        /// </summary>
        public LLMGenerationOptions Options { get; set; } = new LLMGenerationOptions();

        /// <summary>
        /// 上下文信息
        /// </summary>
        public LLMContext Context { get; set; } = new LLMContext();

        /// <summary>
        /// 是否启用工具调用
        /// </summary>
        public bool EnableTools { get; set; } = false;

        /// <summary>
        /// 可用工具列表
        /// </summary>
        public List<LLMTool> AvailableTools { get; set; } = new List<LLMTool>();
    }

    /// <summary>
    /// LLM消息模型 - 支持多模态内容
    /// </summary>
    public class LLMMessage
    {
        /// <summary>
        /// 消息唯一标识符
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 消息角色
        /// </summary>
        public LLMRole Role { get; set; }

        /// <summary>
        /// 文本内容
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// 多模态内容列表
        /// </summary>
        public List<LLMContent> Contents { get; set; } = new List<LLMContent>();

        /// <summary>
        /// 工具调用ID（用于关联工具调用和结果）
        /// </summary>
        public string ToolCallId { get; set; }

        /// <summary>
        /// 消息创建时间
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 消息元数据
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// LLM内容模型 - 支持不同类型的内容
    /// </summary>
    public class LLMContent
    {
        /// <summary>
        /// 内容类型
        /// </summary>
        public LLMContentType Type { get; set; }

        /// <summary>
        /// 文本内容
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// 图像内容
        /// </summary>
        public LLMImageContent Image { get; set; }

        /// <summary>
        /// 音频内容
        /// </summary>
        public LLMAudioContent Audio { get; set; }

        /// <summary>
        /// 文件内容
        /// </summary>
        public LLMFileContent File { get; set; }

        /// <summary>
        /// 其他元数据
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 图像内容模型
    /// </summary>
    public class LLMImageContent
    {
        /// <summary>
        /// 图像数据（Base64编码）
        /// </summary>
        public string Data { get; set; }

        /// <summary>
        /// 图像URL
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// MIME类型
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// 图像描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 图像尺寸
        /// </summary>
        public (int Width, int Height)? Dimensions { get; set; }
    }

    /// <summary>
    /// 音频内容模型
    /// </summary>
    public class LLMAudioContent
    {
        /// <summary>
        /// 音频数据（Base64编码）
        /// </summary>
        public string Data { get; set; }

        /// <summary>
        /// 音频URL
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// MIME类型
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// 音频时长（秒）
        /// </summary>
        public double? Duration { get; set; }

        /// <summary>
        /// 音频转录文本
        /// </summary>
        public string Transcript { get; set; }
    }

    /// <summary>
    /// 文件内容模型
    /// </summary>
    public class LLMFileContent
    {
        /// <summary>
        /// 文件名
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// 文件数据（Base64编码）
        /// </summary>
        public string Data { get; set; }

        /// <summary>
        /// 文件URL
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// MIME类型
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long? Size { get; set; }

        /// <summary>
        /// 文件内容摘要
        /// </summary>
        public string Summary { get; set; }
    }

    /// <summary>
    /// LLM生成选项
    /// </summary>
    public class LLMGenerationOptions
    {
        /// <summary>
        /// 温度参数 (0.0 - 2.0)
        /// </summary>
        public double? Temperature { get; set; }

        /// <summary>
        /// Top-p 采样参数
        /// </summary>
        public double? TopP { get; set; }

        /// <summary>
        /// Top-k 采样参数
        /// </summary>
        public int? TopK { get; set; }

        /// <summary>
        /// 最大生成token数
        /// </summary>
        public int? MaxTokens { get; set; }

        /// <summary>
        /// 停止词列表
        /// </summary>
        public List<string> StopSequences { get; set; } = new List<string>();

        /// <summary>
        /// 频率惩罚 (-2.0 - 2.0)
        /// </summary>
        public double? FrequencyPenalty { get; set; }

        /// <summary>
        /// 存在惩罚 (-2.0 - 2.0)
        /// </summary>
        public double? PresencePenalty { get; set; }

        /// <summary>
        /// 随机种子
        /// </summary>
        public int? Seed { get; set; }

        /// <summary>
        /// 是否启用流式输出
        /// </summary>
        public bool Stream { get; set; } = true;

        /// <summary>
        /// 响应格式
        /// </summary>
        public LLMResponseFormat ResponseFormat { get; set; } = LLMResponseFormat.Text;
    }

    /// <summary>
    /// LLM上下文信息
    /// </summary>
    public class LLMContext
    {
        /// <summary>
        /// 聊天ID
        /// </summary>
        public long ChatId { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 会话ID
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// 语言代码
        /// </summary>
        public string Language { get; set; } = "zh-CN";

        /// <summary>
        /// 时区
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// 自定义属性
        /// </summary>
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }
} 
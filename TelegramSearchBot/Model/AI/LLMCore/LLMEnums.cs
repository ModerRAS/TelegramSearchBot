namespace TelegramSearchBot.Model.AI
{
    /// <summary>
    /// LLM角色枚举
    /// </summary>
    public enum LLMRole
    {
        /// <summary>
        /// 系统角色 - 用于系统提示词
        /// </summary>
        System,

        /// <summary>
        /// 用户角色 - 用户输入
        /// </summary>
        User,

        /// <summary>
        /// 助手角色 - AI响应
        /// </summary>
        Assistant,

        /// <summary>
        /// 工具角色 - 工具执行结果
        /// </summary>
        Tool
    }

    /// <summary>
    /// LLM内容类型枚举
    /// </summary>
    public enum LLMContentType
    {
        /// <summary>
        /// 文本内容
        /// </summary>
        Text,

        /// <summary>
        /// 图像内容
        /// </summary>
        Image,

        /// <summary>
        /// 音频内容
        /// </summary>
        Audio,

        /// <summary>
        /// 视频内容
        /// </summary>
        Video,

        /// <summary>
        /// 文件内容
        /// </summary>
        File,

        /// <summary>
        /// JSON数据
        /// </summary>
        Json,

        /// <summary>
        /// XML数据
        /// </summary>
        Xml
    }

    /// <summary>
    /// LLM响应格式枚举
    /// </summary>
    public enum LLMResponseFormat
    {
        /// <summary>
        /// 纯文本格式
        /// </summary>
        Text,

        /// <summary>
        /// JSON格式
        /// </summary>
        Json,

        /// <summary>
        /// Markdown格式
        /// </summary>
        Markdown,

        /// <summary>
        /// XML格式
        /// </summary>
        Xml,

        /// <summary>
        /// HTML格式
        /// </summary>
        Html
    }

    /// <summary>
    /// 工具调用状态枚举
    /// </summary>
    public enum LLMToolCallStatus
    {
        /// <summary>
        /// 等待执行
        /// </summary>
        Pending,

        /// <summary>
        /// 正在执行
        /// </summary>
        Running,

        /// <summary>
        /// 执行成功
        /// </summary>
        Success,

        /// <summary>
        /// 执行失败
        /// </summary>
        Failed,

        /// <summary>
        /// 执行超时
        /// </summary>
        Timeout,

        /// <summary>
        /// 执行取消
        /// </summary>
        Cancelled
    }

    /// <summary>
    /// 图像分析类型枚举
    /// </summary>
    public enum LLMImageAnalysisType
    {
        /// <summary>
        /// 通用分析
        /// </summary>
        General,

        /// <summary>
        /// OCR文字识别
        /// </summary>
        OCR,

        /// <summary>
        /// 对象检测
        /// </summary>
        ObjectDetection,

        /// <summary>
        /// 场景分析
        /// </summary>
        SceneAnalysis,

        /// <summary>
        /// 情感分析
        /// </summary>
        SentimentAnalysis,

        /// <summary>
        /// 安全检测
        /// </summary>
        SafetyDetection,

        /// <summary>
        /// 医疗分析
        /// </summary>
        Medical,

        /// <summary>
        /// 艺术分析
        /// </summary>
        Art,

        /// <summary>
        /// 技术文档分析
        /// </summary>
        Technical
    }

    /// <summary>
    /// LLM处理优先级枚举
    /// </summary>
    public enum LLMPriority
    {
        /// <summary>
        /// 低优先级
        /// </summary>
        Low = 0,

        /// <summary>
        /// 普通优先级
        /// </summary>
        Normal = 1,

        /// <summary>
        /// 高优先级
        /// </summary>
        High = 2,

        /// <summary>
        /// 紧急优先级
        /// </summary>
        Critical = 3
    }

    /// <summary>
    /// LLM流式更新类型枚举
    /// </summary>
    public enum LLMStreamUpdateType
    {
        /// <summary>
        /// 内容更新
        /// </summary>
        Content,

        /// <summary>
        /// 工具调用开始
        /// </summary>
        ToolCallStart,

        /// <summary>
        /// 工具调用完成
        /// </summary>
        ToolCallComplete,

        /// <summary>
        /// 错误发生
        /// </summary>
        Error,

        /// <summary>
        /// 流式结束
        /// </summary>
        Complete,

        /// <summary>
        /// Token使用统计更新
        /// </summary>
        TokenUsage,

        /// <summary>
        /// 元数据更新
        /// </summary>
        Metadata
    }

    /// <summary>
    /// LLM缓存策略枚举
    /// </summary>
    public enum LLMCacheStrategy
    {
        /// <summary>
        /// 不使用缓存
        /// </summary>
        None,

        /// <summary>
        /// 内存缓存
        /// </summary>
        Memory,

        /// <summary>
        /// Redis缓存
        /// </summary>
        Redis,

        /// <summary>
        /// 文件缓存
        /// </summary>
        File,

        /// <summary>
        /// 数据库缓存
        /// </summary>
        Database
    }

    /// <summary>
    /// LLM安全级别枚举
    /// </summary>
    public enum LLMSecurityLevel
    {
        /// <summary>
        /// 无限制
        /// </summary>
        None,

        /// <summary>
        /// 基础安全检查
        /// </summary>
        Basic,

        /// <summary>
        /// 标准安全检查
        /// </summary>
        Standard,

        /// <summary>
        /// 严格安全检查
        /// </summary>
        Strict,

        /// <summary>
        /// 最高安全级别
        /// </summary>
        Maximum
    }

    /// <summary>
    /// LLM模型能力枚举
    /// </summary>
    [System.Flags]
    public enum LLMCapabilities
    {
        /// <summary>
        /// 无特殊能力
        /// </summary>
        None = 0,

        /// <summary>
        /// 支持文本对话
        /// </summary>
        TextChat = 1 << 0,

        /// <summary>
        /// 支持图像理解
        /// </summary>
        ImageUnderstanding = 1 << 1,

        /// <summary>
        /// 支持音频处理
        /// </summary>
        AudioProcessing = 1 << 2,

        /// <summary>
        /// 支持视频分析
        /// </summary>
        VideoAnalysis = 1 << 3,

        /// <summary>
        /// 支持工具调用
        /// </summary>
        ToolCalling = 1 << 4,

        /// <summary>
        /// 支持代码生成
        /// </summary>
        CodeGeneration = 1 << 5,

        /// <summary>
        /// 支持数学计算
        /// </summary>
        Mathematics = 1 << 6,

        /// <summary>
        /// 支持文档分析
        /// </summary>
        DocumentAnalysis = 1 << 7,

        /// <summary>
        /// 支持翻译
        /// </summary>
        Translation = 1 << 8,

        /// <summary>
        /// 支持摘要生成
        /// </summary>
        Summarization = 1 << 9,

        /// <summary>
        /// 支持情感分析
        /// </summary>
        SentimentAnalysis = 1 << 10,

        /// <summary>
        /// 支持长文本处理
        /// </summary>
        LongContext = 1 << 11,

        /// <summary>
        /// 支持流式输出
        /// </summary>
        Streaming = 1 << 12,

        /// <summary>
        /// 支持嵌入向量生成
        /// </summary>
        Embedding = 1 << 13,

        /// <summary>
        /// 支持微调
        /// </summary>
        FineTuning = 1 << 14,

        /// <summary>
        /// 支持RLHF
        /// </summary>
        RLHF = 1 << 15,

        /// <summary>
        /// 全能力（所有能力的组合）
        /// </summary>
        All = int.MaxValue
    }
} 
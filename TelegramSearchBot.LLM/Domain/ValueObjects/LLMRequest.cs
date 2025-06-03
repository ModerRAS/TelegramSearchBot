namespace TelegramSearchBot.LLM.Domain.ValueObjects;

/// <summary>
/// LLM消息内容类型
/// </summary>
public enum LLMContentType
{
    Text,
    Image,
    Audio,
    File
}

/// <summary>
/// LLM消息角色
/// </summary>
public enum LLMRole
{
    User,
    Assistant,
    System,
    Tool
}

/// <summary>
/// LLM图像内容
/// </summary>
public record LLMImageContent(string? Data = null, string? Url = null, string? MimeType = null);

/// <summary>
/// LLM消息内容
/// </summary>
public record LLMContent(LLMContentType Type, string? Text = null, LLMImageContent? Image = null);

/// <summary>
/// LLM消息
/// </summary>
public record LLMMessage(LLMRole Role, string Content, List<LLMContent>? Contents = null)
{
    public List<LLMContent> Contents { get; init; } = Contents ?? new List<LLMContent>();
}

/// <summary>
/// LLM渠道配置
/// </summary>
public record LLMChannelConfig(
    string Gateway,
    string ApiKey,
    string? OrganizationId = null,
    string? ProxyUrl = null,
    int TimeoutSeconds = 30);

/// <summary>
/// LLM请求值对象
/// </summary>
public record LLMRequest(
    string RequestId,
    string Model,
    LLMChannelConfig Channel,
    List<LLMMessage> ChatHistory,
    string? SystemPrompt = null,
    DateTime? StartTimeParam = null)
{
    public DateTime StartTime { get; init; } = StartTimeParam ?? DateTime.UtcNow;
} 
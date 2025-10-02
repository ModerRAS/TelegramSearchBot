namespace TelegramSearchBot.Vector.Model;

/// <summary>
/// 简单消息DTO，用于避免循环依赖
/// </summary>
public class MessageDto {
    public long Id { get; set; }
    public DateTime DateTime { get; set; }
    public long GroupId { get; set; }
    public long MessageId { get; set; }
    public long FromUserId { get; set; }
    public string? Content { get; set; }
}

using System;
using System.Collections.Generic;

namespace TelegramSearchBot.Application.DTOs.Requests
{
    /// <summary>
    /// 消息数据传输对象
    /// </summary>
    public class MessageDto
    {
        public long Id { get; set; }
        public long GroupId { get; set; }
        public long MessageId { get; set; }
        public long FromUserId { get; set; }
        public string Content { get; set; }
        public DateTime DateTime { get; set; }
        public long ReplyToUserId { get; set; }
        public long ReplyToMessageId { get; set; }
        public IEnumerable<MessageExtensionDto> Extensions { get; set; } = new List<MessageExtensionDto>();
    }

    /// <summary>
    /// 消息扩展数据传输对象
    /// </summary>
    public class MessageExtensionDto
    {
        public long MessageId { get; set; }
        public string ExtensionType { get; set; }
        public string ExtensionData { get; set; }
    }

    /// <summary>
    /// 用户信息数据传输对象
    /// </summary>
    public class UserInfoDto
    {
        public long Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Username { get; set; }
    }

    /// <summary>
    /// 搜索请求数据传输对象
    /// </summary>
    public class SearchRequestDto
    {
        public string Query { get; set; }
        public long? GroupId { get; set; }
        public int Skip { get; set; } = 0;
        public int Take { get; set; } = 20;
    }
}
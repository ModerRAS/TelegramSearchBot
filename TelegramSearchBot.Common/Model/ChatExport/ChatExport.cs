using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TelegramSearchBot.Model.ChatExport
{
    /// <summary>
    /// 聊天导出模型
    /// 用于表示从Telegram导出的聊天数据
    /// </summary>
    public class ChatExport
    {
        /// <summary>
        /// 聊天名称
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// 聊天类型
        /// </summary>
        public string Type { get; set; }
        
        /// <summary>
        /// 聊天ID
        /// </summary>
        public long Id { get; set; }
        
        /// <summary>
        /// 消息列表
        /// </summary>
        public List<Message> Messages { get; set; }
    }

    /// <summary>
    /// 聊天导出消息模型
    /// </summary>
    public class Message
    {
        /// <summary>
        /// 消息ID
        /// </summary>
        public int Id { get; set; }
        
        /// <summary>
        /// 消息类型
        /// </summary>
        public string Type { get; set; }
        
        /// <summary>
        /// 消息日期
        /// </summary>
        public DateTime Date { get; set; }
        
        /// <summary>
        /// Unix时间戳
        /// </summary>
        public string Date_Unixtime { get; set; }
        
        /// <summary>
        /// 发送者名称
        /// </summary>
        public string From { get; set; }
        
        /// <summary>
        /// 发送者ID
        /// </summary>
        public string From_Id { get; set; }
        
        /// <summary>
        /// 文本内容
        /// </summary>
        public List<TextItem> Text { get; set; }
        
        /// <summary>
        /// 文本实体
        /// </summary>
        public List<TextEntity> Text_Entities { get; set; }
        
        /// <summary>
        /// 编辑时间
        /// </summary>
        public string Edited { get; set; }
        
        /// <summary>
        /// 编辑时间Unix时间戳
        /// </summary>
        [JsonProperty("edited_unixtime")]
        public string EditedUnixtime { get; set; }
        
        /// <summary>
        /// 回复的消息ID
        /// </summary>
        [JsonProperty("reply_to_message_id")]
        public int? ReplyToMessageId { get; set; }
        
        /// <summary>
        /// 照片路径
        /// </summary>
        public string Photo { get; set; }
        
        /// <summary>
        /// 照片文件大小
        /// </summary>
        [JsonProperty("photo_file_size")]
        public int? PhotoFileSize { get; set; }
        
        /// <summary>
        /// 宽度
        /// </summary>
        public int? Width { get; set; }
        
        /// <summary>
        /// 高度
        /// </summary>
        public int? Height { get; set; }
        
        /// <summary>
        /// 文件路径
        /// </summary>
        public string File { get; set; }
        
        /// <summary>
        /// 文件名
        /// </summary>
        [JsonProperty("file_name")]
        public string FileName { get; set; }
        
        /// <summary>
        /// 文件大小
        /// </summary>
        [JsonProperty("file_size")]
        public int? FileSize { get; set; }
        
        /// <summary>
        /// 媒体类型
        /// </summary>
        [JsonProperty("media_type")]
        public string MediaType { get; set; }
        
        /// <summary>
        /// MIME类型
        /// </summary>
        [JsonProperty("mime_type")]
        public string MimeType { get; set; }
        
        /// <summary>
        /// 持续时间（秒）
        /// </summary>
        [JsonProperty("duration_seconds")]
        public int? DurationSeconds { get; set; }
    }

    /// <summary>
    /// 文本项
    /// </summary>
    public class TextItem
    {
        /// <summary>
        /// 类型
        /// </summary>
        public string Type { get; set; }
        
        /// <summary>
        /// 文本内容
        /// </summary>
        public string Text { get; set; }
        
        /// <summary>
        /// 链接
        /// </summary>
        public string Href { get; set; }
        
        /// <summary>
        /// 语言
        /// </summary>
        public string Language { get; set; }
    }

    /// <summary>
    /// 文本实体
    /// </summary>
    public class TextEntity
    {
        /// <summary>
        /// 类型
        /// </summary>
        public string Type { get; set; }
        
        /// <summary>
        /// 文本内容
        /// </summary>
        public string Text { get; set; }
        
        /// <summary>
        /// 链接
        /// </summary>
        public string Href { get; set; }
        
        /// <summary>
        /// 语言
        /// </summary>
        public string Language { get; set; }
        
        /// <summary>
        /// 文档ID
        /// </summary>
        [JsonProperty("document_id")]
        public string DocumentId { get; set; }
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramSearchBot.Model.Data
{
    /// <summary>
    /// 对话段模型 - 表示一段连续的对话
    /// </summary>
    public class ConversationSegment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        public long GroupId { get; set; }

        /// <summary>
        /// 对话段开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 对话段结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 第一条消息ID
        /// </summary>
        public long FirstMessageId { get; set; }

        /// <summary>
        /// 最后一条消息ID
        /// </summary>
        public long LastMessageId { get; set; }

        /// <summary>
        /// 消息数量
        /// </summary>
        public int MessageCount { get; set; }

        /// <summary>
        /// 参与对话的用户数量
        /// </summary>
        public int ParticipantCount { get; set; }

        /// <summary>
        /// 对话内容摘要
        /// </summary>
        public string ContentSummary { get; set; }

        /// <summary>
        /// 话题关键词（用逗号分隔）
        /// </summary>
        public string TopicKeywords { get; set; }

        /// <summary>
        /// 对话段的完整文本内容
        /// </summary>
        public string FullContent { get; set; }

        /// <summary>
        /// 向量存储的ID（在Qdrant中的ID）
        /// </summary>
        public string VectorId { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 是否已生成向量
        /// </summary>
        public bool IsVectorized { get; set; } = false;

        /// <summary>
        /// 对话段包含的消息列表
        /// </summary>
        public virtual ICollection<ConversationSegmentMessage> Messages { get; set; }
    }

    /// <summary>
    /// 对话段包含的消息关联表
    /// </summary>
    public class ConversationSegmentMessage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>
        /// 对话段ID
        /// </summary>
        public long ConversationSegmentId { get; set; }

        /// <summary>
        /// 消息数据ID
        /// </summary>
        public long MessageDataId { get; set; }

        /// <summary>
        /// 在对话段中的顺序
        /// </summary>
        public int SequenceOrder { get; set; }

        /// <summary>
        /// 对话段导航属性
        /// </summary>
        public virtual ConversationSegment ConversationSegment { get; set; }

        /// <summary>
        /// 消息导航属性
        /// </summary>
        public virtual Message Message { get; set; }
    }
} 
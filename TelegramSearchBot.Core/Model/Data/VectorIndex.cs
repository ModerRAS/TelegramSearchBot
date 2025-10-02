using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramSearchBot.Model.Data {
    /// <summary>
    /// 向量索引元数据
    /// 存储向量在FAISS索引中的位置和相关信息
    /// </summary>
    public class VectorIndex {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        public long GroupId { get; set; }

        /// <summary>
        /// 向量类型: Message(单消息) 或 ConversationSegment(对话段)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string? VectorType { get; set; }

        /// <summary>
        /// 相关实体ID (MessageId 或 ConversationSegmentId)
        /// </summary>
        public long EntityId { get; set; }

        /// <summary>
        /// 在FAISS索引中的位置
        /// </summary>
        public long FaissIndex { get; set; }

        /// <summary>
        /// 向量内容的摘要（用于调试和展示）
        /// </summary>
        [MaxLength(1000)]
        public string? ContentSummary { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// FAISS索引文件信息
    /// 记录每个群组的索引文件状态
    /// </summary>
    public class FaissIndexFile {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        public long GroupId { get; set; }

        /// <summary>
        /// 索引类型
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string? IndexType { get; set; }

        /// <summary>
        /// 索引文件路径
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string? FilePath { get; set; }

        /// <summary>
        /// 向量维度
        /// </summary>
        public int Dimension { get; set; } = 1024;

        /// <summary>
        /// 当前向量数量
        /// </summary>
        public long VectorCount { get; set; }

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; } = true;
    }
}

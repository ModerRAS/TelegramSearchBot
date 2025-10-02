using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace TelegramSearchBot.Model.Data {
    /// <summary>
    /// 记账记录表
    /// </summary>
    [Index(nameof(AccountBookId), nameof(CreatedAt))]
    [Index(nameof(Tag))]
    public class AccountRecord {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// 所属账本ID
        /// </summary>
        [Required]
        public long AccountBookId { get; set; }

        /// <summary>
        /// 金额（正数为收入，负数为支出）
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        /// <summary>
        /// 标签/分类
        /// </summary>
        [Required]
        [StringLength(50)]
        public string Tag { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        [StringLength(500)]
        public string Description { get; set; }

        /// <summary>
        /// 创建者ID
        /// </summary>
        [Required]
        public long CreatedBy { get; set; }

        /// <summary>
        /// 创建者用户名
        /// </summary>
        [StringLength(100)]
        public string CreatedByUsername { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 关联的账本
        /// </summary>
        [ForeignKey(nameof(AccountBookId))]
        public virtual AccountBook AccountBook { get; set; }
    }
}

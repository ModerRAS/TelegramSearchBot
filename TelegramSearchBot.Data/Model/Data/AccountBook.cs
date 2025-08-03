using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramSearchBot.Model.Data
{
    /// <summary>
    /// 账本表
    /// </summary>
    [Index(nameof(GroupId), nameof(Name), IsUnique = true)]
    public class AccountBook
    {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// 所属群组ID
        /// </summary>
        [Required]
        public long GroupId { get; set; }

        /// <summary>
        /// 账本名称
        /// </summary>
        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        /// <summary>
        /// 账本描述
        /// </summary>
        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// 创建者ID
        /// </summary>
        [Required]
        public long CreatedBy { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 账本记录
        /// </summary>
        public virtual ICollection<AccountRecord> Records { get; set; } = new List<AccountRecord>();
    }
}
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace TelegramSearchBot.Model.Data {
    /// <summary>
    /// 群组记账设置表
    /// </summary>
    [Index(nameof(GroupId), IsUnique = true)]
    public class GroupAccountSettings {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// 群组ID
        /// </summary>
        [Required]
        public long GroupId { get; set; }

        /// <summary>
        /// 当前激活的账本ID
        /// </summary>
        public long? ActiveAccountBookId { get; set; }

        /// <summary>
        /// 是否启用记账功能
        /// </summary>
        public bool IsAccountingEnabled { get; set; } = true;
    }
}

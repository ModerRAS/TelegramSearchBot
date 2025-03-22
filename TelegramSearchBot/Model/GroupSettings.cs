using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model {
    public class GroupSettings {
        [Key]
        public long Id { get; set; }
        public long GroupId { get; set; }
        public string LLMModelName { get; set; }
        /// <summary>
        /// 是否是有管理员权限的群，是的所有群友都可以作为管理员操作一部分功能
        /// </summary>
        public bool IsManagerGroup { get; set; }
    }
}

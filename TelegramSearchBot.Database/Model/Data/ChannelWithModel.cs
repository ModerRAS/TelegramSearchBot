using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model.Data {
    public class ChannelWithModel {
        [Key]
        public int Id { get; set; }
        public string ModelName { get; set; }
        [ForeignKey("LLMChannel")]
        public int LLMChannelId { get; set; }
        public virtual LLMChannel LLMChannel { get; set; }

        /// <summary>
        /// 标记删除：模型在最近一次刷新后不再存在于提供商，但保留记录以供历史查询
        /// </summary>
        public bool IsDeleted { get; set; } = false;

        /// <summary>
        /// 关联的模型能力信息
        /// </summary>
        public virtual ICollection<ModelCapability> Capabilities { get; set; } = new List<ModelCapability>();
    }
}

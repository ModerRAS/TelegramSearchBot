using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model.AI;

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
        /// 关联的 API 绑定（nullable：旧二进制写入的 legacy 行没有绑定，运行时按 channel 默认 binding 解释）
        /// </summary>
        [ForeignKey("ApiBinding")]
        public int? ApiBindingId { get; set; }
        public virtual LLMApiBinding ApiBinding { get; set; }

        /// <summary>
        /// 授权来源：Manual=管理员手工添加（不被刷新软删）；Discovered=来自授权快照
        /// </summary>
        public AuthorizationSource AuthorizationSource { get; set; } = AuthorizationSource.Manual;

        /// <summary>
        /// 模型级协议覆盖：true 时该模型优先使用此 binding，覆盖 channel 默认绑定
        /// </summary>
        public bool IsPreferred { get; set; } = false;

        /// <summary>
        /// 关联的模型能力信息
        /// </summary>
        public virtual ICollection<ModelCapability> Capabilities { get; set; } = new List<ModelCapability>();
    }
}

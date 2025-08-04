using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model.Data 
{
    /// <summary>
    /// 存储LLM模型的能力信息，如工具调用、视觉处理、嵌入等
    /// </summary>
    public class ModelCapability 
    {
        [Key]
        public int Id { get; set; }
        
        /// <summary>
        /// 关联的ChannelWithModel ID
        /// </summary>
        [ForeignKey("ChannelWithModel")]
        public int ChannelWithModelId { get; set; }
        public virtual ChannelWithModel ChannelWithModel { get; set; }
        
        /// <summary>
        /// 能力名称，如 "function_calling", "vision", "embedding" 等
        /// </summary>
        [Required]
        public string CapabilityName { get; set; }
        
        /// <summary>
        /// 能力值，通常为布尔值的字符串表示，或具体的能力描述
        /// </summary>
        public string CapabilityValue { get; set; }
        
        /// <summary>
        /// 能力描述
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
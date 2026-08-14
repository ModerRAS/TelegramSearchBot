using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Model.Data {
    /// <summary>
    /// 一个 channel（品牌/订阅账号）下的一条 API 绑定：endpoint + 协议 + 认证方式。
    /// 每 channel 至多一条 IsDefault=true；secret 仍共享自 LLMChannel.ApiKey。
    /// </summary>
    public class LLMApiBinding {
        [Key]
        public int Id { get; set; }

        [ForeignKey("LLMChannel")]
        public int LLMChannelId { get; set; }
        public virtual LLMChannel LLMChannel { get; set; }

        public string Endpoint { get; set; }
        public LlmProtocol Protocol { get; set; }
        public LlmAuthProfile AuthProfile { get; set; }
        public bool IsDefault { get; set; }
    }
}

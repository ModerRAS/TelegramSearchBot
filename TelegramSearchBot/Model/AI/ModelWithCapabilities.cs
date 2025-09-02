using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model.AI {
    /// <summary>
    /// 包含模型信息和能力的数据传输对象
    /// </summary>
    public class ModelWithCapabilities {
        /// <summary>
        /// 模型名称
        /// </summary>
        public string ModelName { get; set; }

        /// <summary>
        /// 模型能力字典，键为能力名称，值为能力值/描述
        /// </summary>
        public Dictionary<string, string> Capabilities { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// 是否支持工具调用
        /// </summary>
        public bool SupportsToolCalling => GetCapabilityBool("function_calling") || GetCapabilityBool("tool_calls");

        /// <summary>
        /// 是否支持视觉/图像处理
        /// </summary>
        public bool SupportsVision => GetCapabilityBool("vision") || GetCapabilityBool("image_content") || GetCapabilityBool("multimodal");

        /// <summary>
        /// 是否是嵌入模型
        /// </summary>
        public bool IsEmbeddingModel => GetCapabilityBool("embedding") || ModelName.ToLower().Contains("embedding");

        /// <summary>
        /// 是否支持流式响应
        /// </summary>
        public bool SupportsStreaming => GetCapabilityBool("streaming");

        /// <summary>
        /// 是否支持嵌入生成
        /// </summary>
        public bool SupportsEmbedding => GetCapabilityBool("embedding") || GetCapabilityBool("text_embedding");

        /// <summary>
        /// 获取布尔类型的能力值
        /// </summary>
        public bool GetCapabilityBool(string capabilityName) {
            if (Capabilities.TryGetValue(capabilityName, out var value)) {
                return bool.TryParse(value, out var result) && result;
            }
            return false;
        }

        /// <summary>
        /// 获取能力值
        /// </summary>
        public string GetCapability(string capabilityName) {
            return Capabilities.TryGetValue(capabilityName, out var value) ? value : null;
        }

        /// <summary>
        /// 设置能力
        /// </summary>
        public void SetCapability(string capabilityName, string value) {
            Capabilities[capabilityName] = value;
        }

        /// <summary>
        /// 设置布尔能力
        /// </summary>
        public void SetCapability(string capabilityName, bool value) {
            Capabilities[capabilityName] = value.ToString().ToLower();
        }
    }
}

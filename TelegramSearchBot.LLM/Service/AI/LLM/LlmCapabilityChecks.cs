using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>Shared capability checks used by the chat runner path.</summary>
    public static class LlmCapabilityChecks {
        /// <summary>True when the channel/model row declares vision=true capability. Moved from per-service CheckVisionSupport.</summary>
        public static async Task<bool> CheckVisionSupportAsync(DataDbContext dbContext, ILogger logger, string modelName, int channelId) {
            try {
                var channelWithModel = await dbContext.ChannelsWithModel
                    .Include(c => c.Capabilities)
                    .FirstOrDefaultAsync(c => c.ModelName == modelName && c.LLMChannelId == channelId && !c.IsDeleted);

                if (channelWithModel?.Capabilities != null) {
                    return channelWithModel.Capabilities.Any(c =>
                        c.CapabilityName == "vision" && c.CapabilityValue == "true");
                }

                return false;
            } catch (Exception ex) {
                logger.LogDebug(ex, "检查模型视觉能力时出错: {ModelName}", modelName);
                return false;
            }
        }
    }
}

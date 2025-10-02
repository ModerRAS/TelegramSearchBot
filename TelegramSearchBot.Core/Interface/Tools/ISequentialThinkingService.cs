using System.Threading.Tasks;
using TelegramSearchBot.Core.Model;
using TelegramSearchBot.Core.Model.Tools;

namespace TelegramSearchBot.Core.Interface.Tools {
    public interface ISequentialThinkingService {
        Task<object> ProcessThoughtAsync(
            ToolContext toolContext,
            string input,
            bool? nextThoughtNeeded = null,
            int? thoughtNumber = null,
            int? totalThoughts = null,
            bool? isRevision = null,
            int? revisesThought = null,
            int? branchFromThought = null,
            string branchId = null,
            bool? needsMoreThoughts = null);
    }
}

using System.Threading.Tasks;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Interface.Tools {
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

using Orleans;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// Grain interface for processing Bilibili links found in text.
    /// </summary>
    public interface IBilibiliLinkProcessingGrain : IGrainWithGuidKey
    {
        // This grain consumes text, detects Bilibili links, and fetches information.
        // May interact with another grain like IBiliApiServiceGrain.
        // Example of a direct invocation method:
        // Task ProcessBilibiliLinkAsync(string url, MessageContext context);
    }
}

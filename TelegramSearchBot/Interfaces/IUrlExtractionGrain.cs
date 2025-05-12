using Orleans;
using System.Threading.Tasks;
using System.Collections.Generic; // For List
using TelegramSearchBot.Service.Common; // For UrlProcessResult

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// Grain interface for extracting URLs from text content.
    /// It primarily consumes from TextContentToProcess stream for automatic background processing and storage.
    /// It also provides a method for explicit URL resolution on demand.
    /// </summary>
    public interface IUrlExtractionGrain : IGrainWithGuidKey // Using GuidKey as per existing definition
    {
        /// <summary>
        /// Explicitly resolves URLs in the given text and returns a formatted string for display.
        /// This method is intended to be called by CommandParsingGrain for the /resolveurls command.
        /// </summary>
        /// <param name="textToParse">The text containing URLs to resolve.</param>
        /// <returns>A string formatted for user reply, detailing original and resolved URLs.</returns>
        Task<string> GetFormattedResolvedUrlsAsync(string textToParse);

        // The grain will also implicitly subscribe to a stream (e.g., TextContentToProcessStreamName)
        // to perform automatic background URL resolution and storage, without direct reply.
        // No explicit method is needed in the interface for stream consumption if using IAsyncObserver.
    }
}

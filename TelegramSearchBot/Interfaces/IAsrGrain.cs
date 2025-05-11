using Orleans;

namespace TelegramSearchBot.Interfaces
{
    /// <summary>
    /// Grain interface for performing Automatic Speech Recognition (ASR) on audio.
    /// </summary>
    public interface IAsrGrain : IGrainWithGuidKey
    {
        // This grain will consume audio/video streams and produce text.
        // Specific methods can be added if direct invocation is required.
    }
}

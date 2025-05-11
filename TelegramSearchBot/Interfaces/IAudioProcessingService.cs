using System.Threading.Tasks;
using Telegram.Bot; // For ITelegramBotClient
using Telegram.Bot.Types; // For Update

namespace TelegramSearchBot.Interfaces
{
    public interface IAudioProcessingService
    {
        Task<byte[]> ConvertToWavAsync(string filePath);
        Task<byte[]> ConvertToWavAsync(byte[] sourceAudioData);
        bool IsAudio(string fileName);

        // The following methods might need review for suitability in a generic service interface
        // or may require adjustments to their parameters/dependencies (e.g., injecting configuration instead of relying on static Env).
        string GetAudioPath(Update e); // Consider if Update is the right parameter here for a generic service
        Task<byte[]> GetAudioAsync(Update e); // Depends on GetAudioPath

        /// <summary>
        /// Downloads audio from Telegram.
        /// </summary>
        /// <param name="botClient">The Telegram Bot Client instance.</param>
        /// <param name="e">The Telegram Update object containing message details.</param>
        /// <returns>A tuple containing the file name and file data as a byte array.</returns>
        /// <remarks>
        /// The ITelegramBotClient dependency is injected into the service's constructor.
        /// Static Env access for configuration should ideally be replaced by injected configuration.
        /// </remarks>
        Task<(string FileName, byte[] FileData)> DownloadAudioAsync(Update e);
    }
}

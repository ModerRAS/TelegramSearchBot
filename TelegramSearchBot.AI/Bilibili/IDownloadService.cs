using System.Collections.Generic;
using System.Threading.Tasks;

namespace TelegramSearchBot.Service.Bilibili;

public interface IDownloadService
{
    /// <summary>
    /// Downloads a file from the specified URL.
    /// </summary>
    /// <param name="url">The URL of the file to download.</param>
    /// <param name="referer">The referer URL to use for the download request, if any.</param>
    /// <param name="suggestedFileName">A suggested name for the downloaded file (including extension).</param>
    /// <returns>The local path to the downloaded file, or null if download fails.</returns>
    Task<string> DownloadFileAsync(string url, string referer, string suggestedFileName);

    /// <summary>
    /// Downloads DASH stream segments (video and audio) and merges them using FFmpeg.
    /// </summary>
    /// <param name="videoStreamUrl">The URL of the DASH video stream.</param>
    /// <param name="audioStreamUrl">The URL of the DASH audio stream.</param>
    /// <param name="referer">The referer URL to use for download requests.</param>
    /// <param name="outputFileName">The desired name for the merged output file (including extension, e.g., "video.mp4").</param>
    /// <returns>The local path to the merged media file, or null if download or merge fails.</returns>
    Task<string> DownloadAndMergeDashStreamsAsync(string videoStreamUrl, string audioStreamUrl, string referer, string outputFileName);
}

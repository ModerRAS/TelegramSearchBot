using System;
using System.Collections.Generic;
using System.Diagnostics; // Keep for potential future use, but not for ffmpeg call
using System.IO;
using System.Linq; // Added for Any()
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Manager; // For Env.WorkDir
using FFMpegCore; // Added for FFMpeg manipulation
using FFMpegCore.Enums; // Added for SpeedArgument (may not be needed now)

namespace TelegramSearchBot.Service.Bilibili;

[Injectable(ServiceLifetime.Transient)]
public class DownloadService : IDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DownloadService> _logger;
    private readonly string _tempFileDirectory;

    public DownloadService(IHttpClientFactory httpClientFactory, ILogger<DownloadService> logger)
    {
        // Use a dedicated client or BiliApiClient
        _httpClient = httpClientFactory.CreateClient("BiliApiClient"); 
        _logger = logger;

        // Ensure a temporary directory for downloads exists
        _tempFileDirectory = Path.Combine(Env.WorkDir, "temp_downloads");
        Directory.CreateDirectory(_tempFileDirectory);

        // Configure FFMpegCore global settings if needed (e.g., path to ffmpeg/ffprobe)
        // By default, it tries to find them in PATH or common locations.
        // GlobalFFOptions.Configure(new FFOptions { BinaryFolder = "/path/to/ffmpeg/bin" }); 
    }

    public async Task<string> DownloadFileAsync(string url, string referer, string suggestedFileName)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(suggestedFileName))
        {
            _logger.LogWarning("URL or suggested file name is empty for download.");
            return null;
        }

        // Sanitize filename slightly
        var safeFileName = string.Join("_", suggestedFileName.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_tempFileDirectory, $"{Guid.NewGuid()}_{safeFileName}");

        try
        {
            _logger.LogInformation("Starting download: {Url} to {FilePath}", url, filePath);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(referer))
            {
                // Attempt to create a Uri to check if it has a scheme
                if (!Uri.TryCreate(referer, UriKind.Absolute, out Uri refererUri) || string.IsNullOrEmpty(refererUri.Scheme))
                {
                    // If no scheme or invalid URI, prepend https://
                    referer = "https://" + referer;
                }
                request.Headers.Referrer = new Uri(referer);
            }
            // Add User-Agent, consistent with BiliApiService
            if (!request.Headers.UserAgent.Any()) // Add if not already set by a default policy for BiliApiClient
            {
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            await contentStream.CopyToAsync(fileStream);

            _logger.LogInformation("Download completed: {FilePath}", filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from {Url} to {FilePath}", url, filePath);
            // Clean up partially downloaded file
            if (System.IO.File.Exists(filePath)) 
            {
                try { System.IO.File.Delete(filePath); } catch { /* Ignore delete error */ }
            }
            return null;
        }
    }

    public async Task<string> DownloadAndMergeDashStreamsAsync(string videoStreamUrl, string audioStreamUrl, string referer, string outputFileName)
    {
        if (string.IsNullOrWhiteSpace(videoStreamUrl) || string.IsNullOrWhiteSpace(audioStreamUrl) || string.IsNullOrWhiteSpace(outputFileName))
        {
            _logger.LogWarning("One or more required parameters for DASH stream download/merge are empty.");
            return null;
        }
        
        // Sanitize output filename
        var safeOutputFileName = string.Join("_", outputFileName.Split(Path.GetInvalidFileNameChars()));
        var mergedFilePath = Path.Combine(_tempFileDirectory, $"{Guid.NewGuid()}_{safeOutputFileName}");
        string tempVideoFile = null;
        string tempAudioFile = null;

        try
        {
            // Download video and audio streams
            _logger.LogInformation("Downloading DASH video stream: {VideoStreamUrl}", videoStreamUrl);
            tempVideoFile = await DownloadFileAsync(videoStreamUrl, referer, "temp_video.m4s"); // Use appropriate extension if known
            if (tempVideoFile == null) throw new Exception("Failed to download video stream.");

            _logger.LogInformation("Downloading DASH audio stream: {AudioStreamUrl}", audioStreamUrl);
            tempAudioFile = await DownloadFileAsync(audioStreamUrl, referer, "temp_audio.m4s"); // Use appropriate extension if known
            if (tempAudioFile == null) throw new Exception("Failed to download audio stream.");

            _logger.LogInformation("Starting FFMpegCore process to merge streams into {MergedFilePath}", mergedFilePath);

            // Use FFMpegCore to merge the files using stream copy
            bool success = await FFMpegArguments
                .FromFileInput(tempVideoFile)
                .AddFileInput(tempAudioFile)
                .OutputToFile(mergedFilePath, true, options => options
                    .WithCustomArgument("-c:v copy") // Copy video stream without re-encoding
                    .WithCustomArgument("-c:a copy")) // Copy audio stream without re-encoding
                .ProcessAsynchronously();

            if (!success)
            {
                _logger.LogError("FFMpegCore merge process failed for output: {MergedFilePath}", mergedFilePath);
                throw new Exception("FFMpegCore failed to merge streams.");
            }

            _logger.LogInformation("FFMpegCore merge completed successfully: {MergedFilePath}", mergedFilePath);
            return mergedFilePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DownloadAndMergeDashStreamsAsync for output {OutputFileName}", outputFileName);
            // Clean up merged file if it exists from a failed attempt
            if (System.IO.File.Exists(mergedFilePath)) 
            {
                 try { System.IO.File.Delete(mergedFilePath); } catch { /* Ignore delete error */ }
            }
            return null;
        }
        finally
        {
            // Clean up temporary downloaded stream files
            if (tempVideoFile != null && System.IO.File.Exists(tempVideoFile)) 
            {
                try { System.IO.File.Delete(tempVideoFile); } catch { /* Ignore delete error */ }
            }
            if (tempAudioFile != null && System.IO.File.Exists(tempAudioFile)) 
            {
                 try { System.IO.File.Delete(tempAudioFile); } catch { /* Ignore delete error */ }
            }
        }
    }
}

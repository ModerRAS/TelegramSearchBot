using System;
using System.Collections.Generic;
using System.IO;

namespace TelegramSearchBot.Media.Domain.ValueObjects
{
    /// <summary>
    /// 媒体处理结果值对象
    /// </summary>
    public class MediaProcessingResult : IEquatable<MediaProcessingResult>
    {
        public bool Success { get; }
        public string ProcessedFilePath { get; }
        public string ThumbnailPath { get; }
        public long FileSize { get; }
        public string MimeType { get; }
        public string ErrorMessage { get; }
        public string ExceptionType { get; }
        public Dictionary<string, object> AdditionalData { get; }

        private MediaProcessingResult(bool success, string processedFilePath, string thumbnailPath, long fileSize, 
            string mimeType, string errorMessage, string exceptionType, Dictionary<string, object> additionalData)
        {
            Success = success;
            ProcessedFilePath = processedFilePath ?? string.Empty;
            ThumbnailPath = thumbnailPath ?? string.Empty;
            FileSize = fileSize;
            MimeType = mimeType ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            ExceptionType = exceptionType ?? string.Empty;
            AdditionalData = additionalData ?? new Dictionary<string, object>();
        }

        public static MediaProcessingResult CreateSuccess(string processedFilePath, string thumbnailPath = null, 
            long fileSize = 0, string mimeType = null, Dictionary<string, object> additionalData = null)
        {
            if (string.IsNullOrWhiteSpace(processedFilePath))
                throw new ArgumentException("Processed file path cannot be null or empty", nameof(processedFilePath));

            return new MediaProcessingResult(true, processedFilePath, thumbnailPath, fileSize, mimeType, null, null, additionalData);
        }

        public static MediaProcessingResult CreateFailure(string errorMessage, string exceptionType = null, 
            Dictionary<string, object> additionalData = null)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                throw new ArgumentException("Error message cannot be null or empty", nameof(errorMessage));

            return new MediaProcessingResult(false, null, null, 0, null, errorMessage, exceptionType, additionalData);
        }

        public bool FileExists() => Success && !string.IsNullOrWhiteSpace(ProcessedFilePath) && File.Exists(ProcessedFilePath);
        public bool HasThumbnail() => Success && !string.IsNullOrWhiteSpace(ThumbnailPath) && File.Exists(ThumbnailPath);

        public override bool Equals(object obj) => Equals(obj as MediaProcessingResult);
        public bool Equals(MediaProcessingResult other)
        {
            if (other == null) return false;
            return Success == other.Success &&
                   ProcessedFilePath.Equals(other.ProcessedFilePath, StringComparison.OrdinalIgnoreCase) &&
                   FileSize == other.FileSize;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Success, ProcessedFilePath.ToLowerInvariant(), FileSize);
        }
    }
}
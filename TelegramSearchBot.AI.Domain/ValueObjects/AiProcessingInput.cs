using System;
using System.Collections.Generic;

namespace TelegramSearchBot.AI.Domain.ValueObjects
{
    /// <summary>
    /// AI处理输入数据值对象
    /// </summary>
    public class AiProcessingInput : IEquatable<AiProcessingInput>
    {
        public string? Text { get; }
        public byte[]? ImageData { get; }
        public byte[]? AudioData { get; }
        public byte[]? VideoData { get; }
        public string? FilePath { get; }
        public Dictionary<string, object> Metadata { get; }

        public AiProcessingInput(string? text = null, byte[]? imageData = null, byte[]? audioData = null, 
            byte[]? videoData = null, string? filePath = null, Dictionary<string, object>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(text) && imageData == null && audioData == null && 
                videoData == null && string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("At least one input must be provided");
            }

            Text = text;
            ImageData = imageData;
            AudioData = audioData;
            VideoData = videoData;
            FilePath = filePath;
            Metadata = metadata ?? new Dictionary<string, object>();
        }

        public static AiProcessingInput FromText(string text, Dictionary<string, object>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be null or empty", nameof(text));

            return new AiProcessingInput(text: text, metadata: metadata);
        }

        public static AiProcessingInput FromImage(byte[] imageData, Dictionary<string, object>? metadata = null)
        {
            if (imageData == null || imageData.Length == 0)
                throw new ArgumentException("Image data cannot be null or empty", nameof(imageData));

            return new AiProcessingInput(imageData: imageData, metadata: metadata);
        }

        public static AiProcessingInput FromAudio(byte[] audioData, Dictionary<string, object>? metadata = null)
        {
            if (audioData == null || audioData.Length == 0)
                throw new ArgumentException("Audio data cannot be null or empty", nameof(audioData));

            return new AiProcessingInput(audioData: audioData, metadata: metadata);
        }

        public static AiProcessingInput FromVideo(byte[] videoData, Dictionary<string, object>? metadata = null)
        {
            if (videoData == null || videoData.Length == 0)
                throw new ArgumentException("Video data cannot be null or empty", nameof(videoData));

            return new AiProcessingInput(videoData: videoData, metadata: metadata);
        }

        public static AiProcessingInput FromFile(string filePath, Dictionary<string, object>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            return new AiProcessingInput(filePath: filePath, metadata: metadata);
        }

        public bool HasText => !string.IsNullOrWhiteSpace(Text);
        public bool HasImage => ImageData != null && ImageData.Length > 0;
        public bool HasAudio => AudioData != null && AudioData.Length > 0;
        public bool HasVideo => VideoData != null && VideoData.Length > 0;
        public bool HasFile => !string.IsNullOrWhiteSpace(FilePath);

        public override bool Equals(object obj)
        {
            return Equals(obj as AiProcessingInput);
        }

        public bool Equals(AiProcessingInput other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return Text == other.Text &&
                   EqualityComparer<byte[]>.Default.Equals(ImageData, other.ImageData) &&
                   EqualityComparer<byte[]>.Default.Equals(AudioData, other.AudioData) &&
                   EqualityComparer<byte[]>.Default.Equals(VideoData, other.VideoData) &&
                   FilePath == other.FilePath &&
                   Metadata.Equals(other.Metadata);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Text, ImageData, AudioData, VideoData, FilePath, Metadata);
        }

        public static bool operator ==(AiProcessingInput left, AiProcessingInput right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(AiProcessingInput left, AiProcessingInput right)
        {
            return !(left == right);
        }
    }
}
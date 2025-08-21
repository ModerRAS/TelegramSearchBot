using System;
using System.Collections.Generic;

namespace TelegramSearchBot.AI.Domain.ValueObjects
{
    /// <summary>
    /// AI处理结果值对象
    /// </summary>
    public class AiProcessingResult : IEquatable<AiProcessingResult>
    {
        public bool Success { get; }
        public string? Text { get; }
        public byte[]? ResultData { get; }
        public Dictionary<string, object> Metadata { get; }
        public string? ErrorMessage { get; }
        public string? ExceptionType { get; }
        public TimeSpan ProcessingDuration { get; }

        private AiProcessingResult(bool success, string? text, byte[]? resultData, 
            Dictionary<string, object> metadata, string? errorMessage, string? exceptionType, 
            TimeSpan processingDuration)
        {
            Success = success;
            Text = text;
            ResultData = resultData;
            Metadata = metadata ?? new Dictionary<string, object>();
            ErrorMessage = errorMessage;
            ExceptionType = exceptionType;
            ProcessingDuration = processingDuration;
        }

        public static AiProcessingResult SuccessResult(string? text = null, byte[]? resultData = null, 
            Dictionary<string, object>? metadata = null, TimeSpan? processingDuration = null)
        {
            return new AiProcessingResult(true, text, resultData, metadata, null, null, 
                processingDuration ?? TimeSpan.Zero);
        }

        public static AiProcessingResult FailureResult(string errorMessage, string? exceptionType = null, 
            Dictionary<string, object>? metadata = null, TimeSpan? processingDuration = null)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                throw new ArgumentException("Error message cannot be null or empty", nameof(errorMessage));

            return new AiProcessingResult(false, null, null, metadata, errorMessage, exceptionType, 
                processingDuration ?? TimeSpan.Zero);
        }

        public bool HasText => !string.IsNullOrWhiteSpace(Text);
        public bool HasResultData => ResultData != null && ResultData.Length > 0;

        public override bool Equals(object obj)
        {
            return Equals(obj as AiProcessingResult);
        }

        public bool Equals(AiProcessingResult other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            
            return Success == other.Success &&
                   Text == other.Text &&
                   EqualityComparer<byte[]>.Default.Equals(ResultData, other.ResultData) &&
                   Metadata.Equals(other.Metadata) &&
                   ErrorMessage == other.ErrorMessage &&
                   ExceptionType == other.ExceptionType &&
                   ProcessingDuration.Equals(other.ProcessingDuration);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Success, Text, ResultData, Metadata, ErrorMessage, ExceptionType, ProcessingDuration);
        }

        public static bool operator ==(AiProcessingResult left, AiProcessingResult right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(AiProcessingResult left, AiProcessingResult right)
        {
            return !(left == right);
        }
    }
}
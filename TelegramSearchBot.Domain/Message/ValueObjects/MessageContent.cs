using System;
using System.Text.RegularExpressions;

namespace TelegramSearchBot.Domain.Message.ValueObjects
{
    /// <summary>
    /// 消息内容值对象，包含内容验证和清理逻辑
    /// </summary>
    public class MessageContent : IEquatable<MessageContent>
    {
        private const int MaxLength = 5000;
        
        public string Value { get; }
        public int Length => Value.Length;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        
        // 简化实现：添加Text属性以兼容测试代码
        // 原本实现：测试代码应该使用Value属性
        // 简化实现：为了快速修复编译错误，添加Text属性
        public string Text => Value;

        public static MessageContent Empty { get; } = new MessageContent("");

        public MessageContent(string content)
        {
            if (content == null)
                throw new ArgumentException("Content cannot be null", nameof(content));

            var cleanedContent = CleanContent(content);
            
            if (cleanedContent.Length > MaxLength)
                throw new ArgumentException($"Content length cannot exceed {MaxLength} characters", nameof(content));

            Value = cleanedContent;
        }

        private string CleanContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            // 移除多余的空白字符
            content = content.Trim();
            
            // 移除控制字符
            content = Regex.Replace(content, @"\p{C}+", string.Empty);
            
            // 标准化换行符
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // 压缩多个换行符
            content = Regex.Replace(content, "\n{3,}", "\n\n");

            return content;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as MessageContent);
        }

        public bool Equals(MessageContent other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(MessageContent left, MessageContent right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(MessageContent left, MessageContent right)
        {
            return !(left == right);
        }

        public MessageContent Trim()
        {
            return new MessageContent(Value.Trim());
        }

        public MessageContent Substring(int startIndex, int length)
        {
            if (startIndex < 0 || startIndex >= Value.Length)
                throw new ArgumentException("Start index is out of range", nameof(startIndex));
            
            if (length < 0 || startIndex + length > Value.Length)
                throw new ArgumentException("Start index and length must refer to a location within the string", nameof(length));

            return new MessageContent(Value.Substring(startIndex, length));
        }

        public bool Contains(string value)
        {
            if (value == null)
                return false;
            
            return Value.Contains(value);
        }

        public bool StartsWith(string value)
        {
            if (value == null)
                return false;
            
            return Value.StartsWith(value);
        }

        public bool EndsWith(string value)
        {
            if (value == null)
                return false;
            
            return Value.EndsWith(value);
        }
    }
}
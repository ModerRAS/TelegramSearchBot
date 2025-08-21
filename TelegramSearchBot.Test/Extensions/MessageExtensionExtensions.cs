using System;
using System.Linq;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Domain.Tests.Extensions
{
    /// <summary>
    /// MessageExtension实体的扩展方法，用于测试中的Builder模式
    /// </summary>
    public static class MessageExtensionExtensions
    {
        /// <summary>
        /// 获取扩展类型（兼容测试代码中的Type属性）
        /// </summary>
        public static string Type(this MessageExtension extension)
        {
            return extension?.ExtensionType ?? string.Empty;
        }

        /// <summary>
        /// 获取扩展数据（兼容测试代码中的Value属性）
        /// </summary>
        public static string Value(this MessageExtension extension)
        {
            return extension?.ExtensionData ?? string.Empty;
        }

        /// <summary>
        /// 创建新的MessageExtension实例并设置MessageId
        /// </summary>
        public static MessageExtension WithMessageId(this MessageExtension extension, long messageId)
        {
            return new MessageExtension
            {
                Id = extension.Id,
                MessageDataId = messageId,
                ExtensionType = extension.ExtensionType,
                ExtensionData = extension.ExtensionData
            };
        }

        /// <summary>
        /// 创建新的MessageExtension实例并设置ExtensionType
        /// </summary>
        public static MessageExtension WithType(this MessageExtension extension, string extensionType)
        {
            return new MessageExtension
            {
                Id = extension.Id,
                MessageDataId = extension.MessageDataId,
                ExtensionType = extensionType,
                ExtensionData = extension.ExtensionData
            };
        }

        /// <summary>
        /// 创建新的MessageExtension实例并设置ExtensionData
        /// </summary>
        public static MessageExtension WithValue(this MessageExtension extension, string extensionData)
        {
            return new MessageExtension
            {
                Id = extension.Id,
                MessageDataId = extension.MessageDataId,
                ExtensionType = extension.ExtensionType,
                ExtensionData = extensionData
            };
        }

        /// <summary>
        /// 创建新的MessageExtension实例（模拟CreatedAt属性）
        /// </summary>
        public static MessageExtension WithCreatedAt(this MessageExtension extension, DateTime createdAt)
        {
            // MessageExtension没有CreatedAt属性，所以直接返回原实例
            // 在实际应用中，可能需要重新设计模型或使用其他方式跟踪创建时间
            return new MessageExtension
            {
                Id = extension.Id,
                MessageDataId = extension.MessageDataId,
                ExtensionType = extension.ExtensionType,
                ExtensionData = extension.ExtensionData
            };
        }
    }
}
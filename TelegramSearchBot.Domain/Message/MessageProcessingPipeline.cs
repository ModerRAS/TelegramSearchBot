using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Domain.Message
{

    /// <summary>
    /// 消息处理结果
    /// </summary>
    public class MessageProcessingResult
    {
        public bool Success { get; set; }
        public long MessageId { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, object> Metadata { get; set; }

        public static MessageProcessingResult Successful(long messageId, Dictionary<string, object> metadata = null)
        {
            return new MessageProcessingResult
            {
                Success = true,
                MessageId = messageId,
                Metadata = metadata ?? new Dictionary<string, object>()
            };
        }

        public static MessageProcessingResult Failed(string errorMessage, Dictionary<string, object> metadata = null)
        {
            return new MessageProcessingResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                Metadata = metadata ?? new Dictionary<string, object>()
            };
        }
    }

    /// <summary>
    /// 消息处理管道实现
    /// </summary>
    public class MessageProcessingPipeline : IMessageProcessingPipeline
    {
        private readonly IMessageService _messageService;
        private readonly ILogger<MessageProcessingPipeline> _logger;

        public MessageProcessingPipeline(IMessageService messageService, ILogger<MessageProcessingPipeline> logger)
        {
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 处理消息的完整流程
        /// </summary>
        public async Task<MessageProcessingResult> ProcessMessageAsync(MessageOption messageOption)
        {
            try
            {
                _logger.LogInformation("Starting message processing for message {MessageId} from user {UserId}", 
                    messageOption.MessageId, messageOption.UserId);

                // 步骤1：验证消息数据
                var validationResult = ValidateMessage(messageOption);
                if (!validationResult.Success)
                {
                    _logger.LogWarning("Message validation failed: {ErrorMessage}", validationResult.ErrorMessage);
                    return validationResult;
                }

                // 步骤2：预处理消息
                var preprocessedResult = await PreprocessMessageAsync(messageOption);
                if (!preprocessedResult.Success)
                {
                    _logger.LogWarning("Message preprocessing failed: {ErrorMessage}", preprocessedResult.ErrorMessage);
                    return preprocessedResult;
                }

                // 步骤3：处理消息
                var messageId = await _messageService.ProcessMessageAsync(messageOption);

                // 步骤4：后处理消息
                var postprocessedResult = await PostprocessMessageAsync(messageId, messageOption);
                if (!postprocessedResult.Success)
                {
                    _logger.LogWarning("Message postprocessing failed: {ErrorMessage}", postprocessedResult.ErrorMessage);
                    return postprocessedResult;
                }

                // 步骤5：索引消息（如果需要）
                var indexingResult = await IndexMessageAsync(messageId, messageOption);
                if (!indexingResult.Success)
                {
                    _logger.LogWarning("Message indexing failed: {ErrorMessage}", indexingResult.ErrorMessage);
                    // 索引失败不影响整体处理结果
                }

                var metadata = new Dictionary<string, object>
                {
                    { "ProcessingTime", DateTime.UtcNow },
                    { "PreprocessingSuccess", preprocessedResult.Success },
                    { "PostprocessingSuccess", postprocessedResult.Success },
                    { "IndexingSuccess", indexingResult.Success }
                };

                _logger.LogInformation("Successfully processed message {MessageId}", messageId);

                return MessageProcessingResult.Successful(messageId, metadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message {MessageId}", messageOption?.MessageId);
                return MessageProcessingResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// 批量处理消息
        /// </summary>
        public async Task<IEnumerable<MessageProcessingResult>> ProcessMessagesAsync(IEnumerable<MessageOption> messageOptions)
        {
            if (messageOptions == null)
                throw new ArgumentNullException(nameof(messageOptions));

            var results = new List<MessageProcessingResult>();
            var processingTasks = new List<Task<MessageProcessingResult>>();

            foreach (var messageOption in messageOptions)
            {
                processingTasks.Add(ProcessMessageAsync(messageOption));
            }

            var processedResults = await Task.WhenAll(processingTasks);
            results.AddRange(processedResults);

            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count(r => !r.Success);

            _logger.LogInformation("Batch processing completed: {SuccessCount} successful, {FailureCount} failed", 
                successCount, failureCount);

            return results;
        }

        /// <summary>
        /// 验证消息数据
        /// </summary>
        private MessageProcessingResult ValidateMessage(MessageOption messageOption)
        {
            if (messageOption == null)
                return MessageProcessingResult.Failed("Message option is null");

            if (messageOption.ChatId <= 0)
                return MessageProcessingResult.Failed("Invalid chat ID");

            if (messageOption.UserId <= 0)
                return MessageProcessingResult.Failed("Invalid user ID");

            if (messageOption.MessageId <= 0)
                return MessageProcessingResult.Failed("Invalid message ID");

            if (string.IsNullOrWhiteSpace(messageOption.Content))
                return MessageProcessingResult.Failed("Message content is empty");

            if (messageOption.DateTime == default)
                return MessageProcessingResult.Failed("Message datetime is invalid");

            return MessageProcessingResult.Successful(0, new Dictionary<string, object>
            {
                { "ValidationTime", DateTime.UtcNow }
            });
        }

        /// <summary>
        /// 预处理消息
        /// </summary>
        private async Task<MessageProcessingResult> PreprocessMessageAsync(MessageOption messageOption)
        {
            try
            {
                // 清理消息内容
                var cleanedContent = CleanMessageContent(messageOption.Content);
                
                // 检查消息长度
                if (cleanedContent.Length > 4000) // Telegram消息长度限制
                {
                    cleanedContent = cleanedContent.Substring(0, 4000);
                }

                // 创建预处理后的消息选项
                var preprocessedOption = new MessageOption
                {
                    ChatId = messageOption.ChatId,
                    UserId = messageOption.UserId,
                    MessageId = messageOption.MessageId,
                    Content = cleanedContent,
                    DateTime = messageOption.DateTime,
                    ReplyTo = messageOption.ReplyTo,
                    User = messageOption.User,
                    Chat = messageOption.Chat
                };

                var result = MessageProcessingResult.Successful(0, new Dictionary<string, object>
                {
                    { "PreprocessingTime", DateTime.UtcNow },
                    { "OriginalLength", messageOption.Content.Length },
                    { "CleanedLength", cleanedContent.Length }
                });
                
                // 创建扩展结果并设置MessageOption
                var extendedResult = new ExtendedMessageProcessingResult
                {
                    Success = result.Success,
                    MessageId = result.MessageId,
                    ErrorMessage = result.ErrorMessage,
                    Metadata = result.Metadata,
                    MessageOption = preprocessedOption
                };
                
                return extendedResult;
            }
            catch (Exception ex)
            {
                return MessageProcessingResult.Failed($"Preprocessing failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 后处理消息
        /// </summary>
        private async Task<MessageProcessingResult> PostprocessMessageAsync(long messageId, MessageOption messageOption)
        {
            try
            {
                // 这里可以添加后处理逻辑，例如：
                // - 发送通知
                // - 触发其他服务
                // - 更新统计信息

                return MessageProcessingResult.Successful(messageId, new Dictionary<string, object>
                {
                    { "PostprocessingTime", DateTime.UtcNow }
                });
            }
            catch (Exception ex)
            {
                return MessageProcessingResult.Failed($"Postprocessing failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 索引消息
        /// </summary>
        private async Task<MessageProcessingResult> IndexMessageAsync(long messageId, MessageOption messageOption)
        {
            try
            {
                // 这里可以添加索引逻辑，例如：
                // - 添加到搜索索引
                // - 生成向量嵌入
                // - 更新缓存

                return MessageProcessingResult.Successful(messageId, new Dictionary<string, object>
                {
                    { "IndexingTime", DateTime.UtcNow }
                });
            }
            catch (Exception ex)
            {
                return MessageProcessingResult.Failed($"Indexing failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理消息内容
        /// </summary>
        private string CleanMessageContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            // 移除多余的空白字符
            content = content.Trim();
            
            // 移除控制字符
            content = System.Text.RegularExpressions.Regex.Replace(content, @"\p{C}+", string.Empty);
            
            // 标准化换行符
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // 压缩多个换行符
            content = System.Text.RegularExpressions.Regex.Replace(content, "\n{3,}", "\n\n");

            return content;
        }
    }

    /// <summary>
    /// 扩展的MessageProcessingResult，用于预处理阶段
    /// </summary>
    public class ExtendedMessageProcessingResult : MessageProcessingResult
    {
        public MessageOption MessageOption { get; set; }
    }
}
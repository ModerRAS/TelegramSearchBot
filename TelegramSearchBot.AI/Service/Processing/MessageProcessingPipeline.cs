using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediatR;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model.Notifications;
using TelegramSearchBot.Interface;

namespace TelegramSearchBot.AI.Service.Processing
{
    /// <summary>
    /// 消息处理管道，负责处理消息的完整生命周期
    /// </summary>
    /// <remarks>
    /// 该类实现了消息的完整处理流程，包括：
    /// - 消息验证
    /// - 消息存储
    /// - Lucene索引
    /// - 错误处理
    /// - 统计信息收集
    /// </remarks>
    public class MessageProcessingPipeline
    {
        private readonly ILogger<MessageProcessingPipeline> _logger;
        private readonly IMessageService _messageService;
        private readonly IMediator _mediator;
        private readonly ILuceneManager _luceneManager;
        private readonly ISendMessageService _sendMessageService;
        
        // 统计信息字段
        private int _totalProcessed;
        private int _successful;
        private int _failed;
        private double _totalProcessingTimeMs;
        private DateTime _lastProcessed;

        public MessageProcessingPipeline(
            ILogger<MessageProcessingPipeline> logger,
            IMessageService messageService,
            IMediator mediator,
            ILuceneManager luceneManager,
            ISendMessageService sendMessageService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _luceneManager = luceneManager ?? throw new ArgumentNullException(nameof(luceneManager));
            _sendMessageService = sendMessageService ?? throw new ArgumentNullException(nameof(sendMessageService));
        }

        /// <summary>
        /// 处理单个消息
        /// </summary>
        /// <param name="messageOption">消息选项，包含消息的基本信息和内容</param>
        /// <param name="cancellationToken">取消令牌，用于取消异步操作</param>
        /// <returns>处理结果，包含成功状态、消息ID和可能的警告信息</returns>
        /// <exception cref="ArgumentNullException">当messageOption为null时抛出</exception>
        public async Task<MessageProcessingResult> ProcessMessageAsync(MessageOption messageOption, CancellationToken cancellationToken = default)
        {
            if (messageOption == null)
            {
                throw new ArgumentNullException(nameof(messageOption));
            }

            var startTime = DateTime.UtcNow;
            var result = new MessageProcessingResult
            {
                ProcessedAt = startTime,
                MessageId = messageOption.MessageId
            };

            try
            {
                _logger.LogInformation("Processing message {MessageId} from user {UserId} in chat {ChatId}", 
                    messageOption.MessageId, messageOption.UserId, messageOption.ChatId);

                // 验证消息
                var validationResult = ValidateMessage(messageOption);
                if (!validationResult.IsValid)
                {
                    result.Success = false;
                    result.Message = $"Validation failed: {string.Join(", ", validationResult.Errors)}";
                    UpdateStatistics(false, DateTime.UtcNow - startTime);
                    return result;
                }

                // 检查大消息
                if (messageOption.Content?.Length > 5000)
                {
                    _logger.LogWarning("Large message detected: {MessageId} with {Length} characters", 
                        messageOption.MessageId, messageOption.Content.Length);
                    result.Warnings.Add("Large message detected");
                }

                // 处理消息
                var messageId = await _messageService.ExecuteAsync(messageOption);
                result.MessageId = messageId;

                // 添加到Lucene索引
                try
                {
                    var message = new Message
                    {
                        Id = messageId,
                        GroupId = messageOption.ChatId,
                        MessageId = messageOption.MessageId,
                        FromUserId = messageOption.UserId,
                        Content = messageOption.Content,
                        DateTime = messageOption.DateTime
                    };
                    await _luceneManager.WriteDocumentAsync(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding message to Lucene: {MessageId}", messageId);
                    result.Warnings.Add($"Lucene indexing failed: {ex.Message}");
                }

                result.Success = true;
                result.Message = "Message processed successfully";
                UpdateStatistics(true, DateTime.UtcNow - startTime);
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "Processing was cancelled";
                _logger.LogWarning("Message processing cancelled: {MessageId}", messageOption.MessageId);
                UpdateStatistics(false, DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Processing failed: {ex.Message}";
                _logger.LogError(ex, "Error processing message: {MessageId}", messageOption.MessageId);
                UpdateStatistics(false, DateTime.UtcNow - startTime);
            }

            return result;
        }

        /// <summary>
        /// 批量处理消息
        /// </summary>
        /// <param name="messageOptions">消息选项列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理结果列表</returns>
        public async Task<List<MessageProcessingResult>> ProcessMessagesAsync(IEnumerable<MessageOption> messageOptions, CancellationToken cancellationToken = default)
        {
            if (messageOptions == null)
            {
                throw new ArgumentNullException(nameof(messageOptions));
            }

            var messageList = messageOptions.ToList();
            _logger.LogInformation("Processing batch of {Count} messages", messageList.Count);

            var results = new List<MessageProcessingResult>();
            var tasks = new List<Task<MessageProcessingResult>>();

            // 并行处理消息，限制并发数
            using (var semaphore = new SemaphoreSlim(10))
            {
                foreach (var messageOption in messageList)
                {
                    await semaphore.WaitAsync(cancellationToken);
                    
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var result = await ProcessMessageAsync(messageOption, cancellationToken);
                            return result;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }

                try
                {
                    var allResults = await Task.WhenAll(tasks);
                    results.AddRange(allResults);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing batch messages");
                    
                    // 收集已完成的任务结果
                    foreach (var task in tasks)
                    {
                        if (task.Status == TaskStatus.RanToCompletion)
                        {
                            results.Add(task.Result);
                        }
                        else if (task.Status == TaskStatus.Faulted)
                        {
                            results.Add(new MessageProcessingResult
                            {
                                Success = false,
                                Message = $"Task failed: {task.Exception?.Message}",
                                ProcessedAt = DateTime.UtcNow
                            });
                        }
                        else if (task.Status == TaskStatus.Canceled)
                        {
                            results.Add(new MessageProcessingResult
                            {
                                Success = false,
                                Message = "Task was cancelled",
                                ProcessedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 验证消息
        /// </summary>
        /// <param name="messageOption">消息选项</param>
        /// <returns>验证结果</returns>
        public MessageValidationResult ValidateMessage(MessageOption messageOption)
        {
            var errors = new List<string>();

            if (messageOption == null)
            {
                errors.Add("Message cannot be null");
                return new MessageValidationResult { IsValid = false, Errors = errors };
            }

            if (messageOption.UserId <= 0)
            {
                errors.Add("Invalid user ID");
            }

            if (messageOption.ChatId <= 0)
            {
                errors.Add("Invalid chat ID");
            }

            if (string.IsNullOrWhiteSpace(messageOption.Content))
            {
                errors.Add("Message content cannot be empty");
            }

            if (messageOption.Content?.Length > 10000)
            {
                errors.Add("Message content exceeds maximum length");
            }

            return new MessageValidationResult
            {
                IsValid = !errors.Any(),
                Errors = errors
            };
        }

        /// <summary>
        /// 获取处理统计信息
        /// </summary>
        /// <returns>统计信息</returns>
        public ProcessingStatistics GetProcessingStatistics()
        {
            return new ProcessingStatistics
            {
                TotalProcessed = _totalProcessed,
                Successful = _successful,
                Failed = _failed,
                AverageProcessingTimeMs = _totalProcessed > 0 ? _totalProcessingTimeMs / _totalProcessed : 0,
                LastProcessed = _lastProcessed
            };
        }

        /// <summary>
        /// 更新统计信息
        /// </summary>
        /// <param name="success">是否成功</param>
        /// <param name="processingTime">处理时间</param>
        private void UpdateStatistics(bool success, TimeSpan processingTime)
        {
            lock (this)
            {
                _totalProcessed++;
                if (success)
                {
                    _successful++;
                }
                else
                {
                    _failed++;
                }
                
                _totalProcessingTimeMs += processingTime.TotalMilliseconds;
                _lastProcessed = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// 消息处理结果
    /// </summary>
    public class MessageProcessingResult
    {
        public bool Success { get; set; }
        public long MessageId { get; set; }
        public string Message { get; set; }
        public DateTime ProcessedAt { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// 消息验证结果
    /// </summary>
    public class MessageValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// 处理统计信息
    /// </summary>
    public class ProcessingStatistics
    {
        public int TotalProcessed { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public double AverageProcessingTimeMs { get; set; }
        public DateTime LastProcessed { get; set; }
    }
}
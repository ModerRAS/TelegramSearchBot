using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Application.Adapters
{
    /// <summary>
    /// Message仓储适配器接口，提供DDD仓储和传统仓储的统一访问
    /// </summary>
    public interface IMessageRepositoryAdapter
    {
        // DDD仓储接口方法
        Task<MessageAggregate> GetByIdAsync(MessageId id, CancellationToken cancellationToken = default);
        Task<IEnumerable<MessageAggregate>> GetByGroupIdAsync(long groupId, CancellationToken cancellationToken = default);
        Task<MessageAggregate> AddAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default);
        Task UpdateAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default);
        Task DeleteAsync(MessageId id, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(MessageId id, CancellationToken cancellationToken = default);
        Task<int> CountByGroupIdAsync(long groupId, CancellationToken cancellationToken = default);
        Task<IEnumerable<MessageAggregate>> SearchAsync(long groupId, string query, int limit = 50, CancellationToken cancellationToken = default);

        // 兼容旧代码的方法
        Task<long> AddMessageAsync(Message message);
        Task<Message> GetMessageByIdAsync(long id);
        Task<List<Message>> GetMessagesByGroupIdAsync(long groupId);
        Task<List<Message>> SearchMessagesAsync(string query, long groupId);
        Task<List<Message>> GetMessagesByUserAsync(long userId);
    }
}
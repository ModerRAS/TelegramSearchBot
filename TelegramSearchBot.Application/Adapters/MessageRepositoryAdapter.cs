using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Application.Adapters
{
    /// <summary>
    /// Message仓储适配器，用于桥接DDD仓储接口和现有代码
    /// </summary>
    public class MessageRepositoryAdapter : IMessageRepositoryAdapter
    {
        private readonly IMessageRepository _dddRepository;
        private readonly IMapper _mapper;

        public MessageRepositoryAdapter(IMessageRepository dddRepository, IMapper mapper)
        {
            _dddRepository = dddRepository ?? throw new ArgumentNullException(nameof(dddRepository));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        #region DDD仓储接口实现

        public async Task<MessageAggregate> GetByIdAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.GetByIdAsync(id, cancellationToken);
        }

        public async Task<IEnumerable<MessageAggregate>> GetByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.GetByGroupIdAsync(groupId, cancellationToken);
        }

        public async Task<MessageAggregate> AddAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.AddAsync(aggregate, cancellationToken);
        }

        public async Task UpdateAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            await _dddRepository.UpdateAsync(aggregate, cancellationToken);
        }

        public async Task DeleteAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            await _dddRepository.DeleteAsync(id, cancellationToken);
        }

        public async Task<bool> ExistsAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.ExistsAsync(id, cancellationToken);
        }

        public async Task<int> CountByGroupIdAsync(long groupId, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.CountByGroupIdAsync(groupId, cancellationToken);
        }

        public async Task<IEnumerable<MessageAggregate>> SearchAsync(long groupId, string query, int limit = 50, CancellationToken cancellationToken = default)
        {
            return await _dddRepository.SearchAsync(groupId, query, limit, cancellationToken);
        }

        #endregion

        #region 兼容旧代码的方法实现

        public async Task<long> AddMessageAsync(Message message)
        {
            var aggregate = _mapper.Map<MessageAggregate>(message);
            var result = await _dddRepository.AddAsync(aggregate);
            return result.Id.TelegramMessageId;
        }

        public async Task<Message> GetMessageByIdAsync(long id)
        {
            // 需要根据实际情况构造MessageId
            var messageId = new MessageId(0, id); // 注意：这可能需要调整
            var aggregate = await _dddRepository.GetByIdAsync(messageId);
            return _mapper.Map<Message>(aggregate);
        }

        public async Task<List<Message>> GetMessagesByGroupIdAsync(long groupId)
        {
            var aggregates = await _dddRepository.GetByGroupIdAsync(groupId);
            return aggregates.Select(a => _mapper.Map<Message>(a)).ToList();
        }

        public async Task<List<Message>> SearchMessagesAsync(string query, long groupId)
        {
            var aggregates = await _dddRepository.SearchAsync(groupId, query);
            return aggregates.Select(a => _mapper.Map<Message>(a)).ToList();
        }

        public async Task<List<Message>> GetMessagesByUserAsync(long userId)
        {
            // 实现用户消息查询逻辑
            var allMessages = await _dddRepository.GetByGroupIdAsync(0); // 需要调整
            return allMessages
                .Where(m => m.Metadata.FromUserId == userId)
                .Select(a => _mapper.Map<Message>(a))
                .ToList();
        }

        #endregion
    }
}
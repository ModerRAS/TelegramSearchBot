using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Infrastructure.Search.Repositories
{
    /// <summary>
    /// 消息搜索仓储的Lucene实现
    /// </summary>
    public class MessageSearchRepository : IMessageSearchRepository
    {
        private readonly ILuceneManager _luceneManager;

        public MessageSearchRepository(ILuceneManager luceneManager)
        {
            _luceneManager = luceneManager ?? throw new ArgumentNullException(nameof(luceneManager));
        }

        public async Task<IEnumerable<MessageSearchResult>> SearchAsync(
            MessageSearchQuery query, 
            CancellationToken cancellationToken = default)
        {
            // 使用现有的LuceneManager进行搜索
            var (totalCount, messages) = await _luceneManager.Search(query.Query, query.GroupId, 0, query.Limit);
            
            return messages.Select(message => new MessageSearchResult(
                new MessageId(message.GroupId, message.MessageId),
                message.Content ?? string.Empty,
                message.DateTime,
                1.0f // 简化实现：Lucene没有直接返回分数，使用固定分数1.0f
            ));
        }

        public async Task IndexAsync(MessageAggregate aggregate, CancellationToken cancellationToken = default)
        {
            // 将领域聚合转换为Message实体
            var message = new Message
            {
                GroupId = aggregate.Id.ChatId,
                MessageId = aggregate.Id.TelegramMessageId,
                Content = aggregate.Content.Value,
                DateTime = aggregate.Metadata.Timestamp,
                FromUserId = aggregate.Metadata.FromUserId
            };

            await _luceneManager.WriteDocumentAsync(message);
        }

        public async Task RemoveFromIndexAsync(MessageId id, CancellationToken cancellationToken = default)
        {
            await _luceneManager.DeleteDocumentAsync(id.ChatId, id.TelegramMessageId);
        }

        public async Task RebuildIndexAsync(IEnumerable<MessageAggregate> messages, CancellationToken cancellationToken = default)
        {
            var messageList = messages.Select(aggregate => new Message
            {
                GroupId = aggregate.Id.ChatId,
                MessageId = aggregate.Id.TelegramMessageId,
                Content = aggregate.Content.Value,
                DateTime = aggregate.Metadata.Timestamp,
                FromUserId = aggregate.Metadata.FromUserId
            }).ToList();

            await _luceneManager.WriteDocuments(messageList);
        }

        public async Task<IEnumerable<MessageSearchResult>> SearchByUserAsync(
            MessageSearchByUserQuery query, 
            CancellationToken cancellationToken = default)
        {
            // 简化实现：使用语法搜索来按用户搜索
            // 原本实现：应该构建专门的用户查询对象和Lucene查询
            // 简化实现：直接拼接查询字符串
            var userQuery = $"from_user:{query.UserId} {query.Query}";
            var (totalCount, messages) = await _luceneManager.SyntaxSearch(userQuery, query.GroupId, 0, query.Limit);
            
            return messages.Select(message => new MessageSearchResult(
                new MessageId(message.GroupId, message.MessageId),
                message.Content ?? string.Empty,
                message.DateTime,
                1.0f // 简化实现：Lucene没有直接返回分数，使用固定分数1.0f
            ));
        }

        public async Task<IEnumerable<MessageSearchResult>> SearchByDateRangeAsync(
            MessageSearchByDateRangeQuery query, 
            CancellationToken cancellationToken = default)
        {
            // 简化实现：使用语法搜索来按日期范围搜索
            // 原本实现：应该构建专门的日期范围查询对象和Lucene查询
            // 简化实现：直接拼接查询字符串
            var dateQuery = $"date:[{query.StartDate:yyyy-MM-dd} TO {query.EndDate:yyyy-MM-dd}] {query.Query}";
            var (totalCount, messages) = await _luceneManager.SyntaxSearch(dateQuery, query.GroupId, 0, query.Limit);
            
            return messages.Select(message => new MessageSearchResult(
                new MessageId(message.GroupId, message.MessageId),
                message.Content ?? string.Empty,
                message.DateTime,
                1.0f // 简化实现：Lucene没有直接返回分数，使用固定分数1.0f
            ));
        }
    }
}
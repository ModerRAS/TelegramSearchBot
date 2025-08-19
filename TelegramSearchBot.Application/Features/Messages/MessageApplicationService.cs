using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using TelegramSearchBot.Application.Abstractions;
using TelegramSearchBot.Application.DTOs.Requests;
using TelegramSearchBot.Application.DTOs.Responses;
using TelegramSearchBot.Application.Exceptions;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.Repositories;
using TelegramSearchBot.Domain.Message.ValueObjects;

namespace TelegramSearchBot.Application.Features.Messages
{
    /// <summary>
    /// 消息应用服务实现
    /// </summary>
    public class MessageApplicationService : IMessageApplicationService
    {
        private readonly IMessageRepository _messageRepository;
        private readonly IMessageSearchRepository _messageSearchRepository;
        private readonly IMediator _mediator;

        public MessageApplicationService(
            IMessageRepository messageRepository,
            IMessageSearchRepository messageSearchRepository,
            IMediator mediator)
        {
            _messageRepository = messageRepository;
            _messageSearchRepository = messageSearchRepository;
            _mediator = mediator;
        }

        public async Task<long> CreateMessageAsync(CreateMessageCommand command)
        {
            // 验证输入
            if (command.MessageDto == null)
                throw new ValidationException(new[] { "Message data cannot be null" });

            // 使用领域工厂创建聚合
            var messageAggregate = MessageAggregate.Create(
                command.GroupId,
                command.MessageDto.MessageId,
                command.MessageDto.Content,
                command.MessageDto.FromUserId,
                command.MessageDto.DateTime);

            // 处理回复信息
            if (command.MessageDto.ReplyToMessageId > 0)
            {
                messageAggregate.UpdateReply(
                    command.MessageDto.ReplyToUserId,
                    command.MessageDto.ReplyToMessageId);
            }

            // 保存到数据库
            await _messageRepository.AddAsync(messageAggregate);

            // 索引到搜索引擎
            await _messageSearchRepository.IndexAsync(messageAggregate);

            // 发布领域事件
            foreach (var domainEvent in messageAggregate.DomainEvents)
            {
                await _mediator.Publish(domainEvent);
            }
            messageAggregate.ClearDomainEvents();

            // 返回数据库生成的ID（这里简化处理）
            return messageAggregate.Id.TelegramMessageId;
        }

        public async Task UpdateMessageAsync(UpdateMessageCommand command)
        {
            // 使用命令中的GroupId
            var messageId = new MessageId(command.GroupId, command.Id);
            var existingMessage = await _messageRepository.GetByIdAsync(messageId);
            if (existingMessage == null)
                throw new MessageNotFoundException(command.Id);

            // 更新内容
            existingMessage.UpdateContent(new MessageContent(command.MessageDto.Content));

            // 保存更改
            await _messageRepository.UpdateAsync(existingMessage);

            // 更新搜索索引
            await _messageSearchRepository.IndexAsync(existingMessage);

            // 发布领域事件
            foreach (var domainEvent in existingMessage.DomainEvents)
            {
                await _mediator.Publish(domainEvent);
            }
            existingMessage.ClearDomainEvents();
        }

        public async Task DeleteMessageAsync(DeleteMessageCommand command)
        {
            var messageId = new MessageId(command.GroupId, command.Id);
            var existingMessage = await _messageRepository.GetByIdAsync(messageId);
            if (existingMessage == null)
                throw new MessageNotFoundException(command.Id);

            // 从数据库删除
            await _messageRepository.DeleteAsync(messageId);

            // 从搜索索引删除
            await _messageSearchRepository.RemoveFromIndexAsync(messageId);

            // 发布领域事件
            foreach (var domainEvent in existingMessage.DomainEvents)
            {
                await _mediator.Publish(domainEvent);
            }
            existingMessage.ClearDomainEvents();
        }

        public async Task<MessageDto> GetMessageByIdAsync(GetMessageByIdQuery query)
        {
            var messageId = new MessageId(query.GroupId, query.Id);
            var message = await _messageRepository.GetByIdAsync(messageId);
            if (message == null)
                throw new MessageNotFoundException(query.Id);

            return MapToMessageDto(message);
        }

        public async Task<IEnumerable<MessageDto>> GetMessagesByGroupAsync(GetMessagesByGroupQuery query)
        {
            var messages = await _messageRepository.GetByGroupIdAsync(query.GroupId);
            
            return messages
                .Skip(query.Skip)
                .Take(query.Take)
                .Select(MapToMessageDto)
                .ToList();
        }

        public async Task<SearchResponseDto> SearchMessagesAsync(SearchMessagesQuery query)
        {
            // 构建搜索查询
            var searchQuery = new MessageSearchQuery(
                query.GroupId ?? 1,
                query.Query,
                query.Take);

            // 执行搜索
            var searchResults = await _messageSearchRepository.SearchAsync(searchQuery);
            
            return new SearchResponseDto
            {
                Messages = searchResults
                    .Skip(query.Skip)
                    .Take(query.Take)
                    .Select(MapToMessageResponseDto)
                    .ToList(),
                TotalCount = searchResults.Count(),
                Skip = query.Skip,
                Take = query.Take,
                Query = query.Query
            };
        }

        // 私有映射方法 - 从领域聚合映射到DTO
        private MessageDto MapToMessageDto(MessageAggregate message)
        {
            return new MessageDto
            {
                Id = message.Id.TelegramMessageId,
                GroupId = message.Id.ChatId,
                MessageId = message.Id.TelegramMessageId,
                FromUserId = message.Metadata.FromUserId,
                Content = message.Content.Value,
                DateTime = message.Metadata.Timestamp,
                ReplyToUserId = message.Metadata.ReplyToUserId,
                ReplyToMessageId = message.Metadata.ReplyToMessageId,
                Extensions = new List<MessageExtensionDto>() // 简化实现：暂时不处理扩展数据
            };
        }

        // 私有映射方法 - 从搜索结果映射到响应DTO
        private MessageResponseDto MapToMessageResponseDto(MessageSearchResult result)
        {
            return new MessageResponseDto
            {
                Id = result.MessageId.TelegramMessageId,
                GroupId = result.MessageId.ChatId,
                MessageId = result.MessageId.TelegramMessageId,
                Content = result.Content,
                DateTime = result.Timestamp,
                Score = result.Score,
                FromUser = new UserInfoDto
                {
                    Id = 0 // 简化实现：暂时不处理用户详细信息
                },
                Extensions = new List<MessageExtensionDto>() // 简化实现：暂时不处理扩展数据
            };
        }

        // 重载方法 - 从领域聚合映射到响应DTO
        private MessageResponseDto MapToMessageResponseDto(MessageAggregate message)
        {
            return new MessageResponseDto
            {
                Id = message.Id.TelegramMessageId,
                GroupId = message.Id.ChatId,
                MessageId = message.Id.TelegramMessageId,
                Content = message.Content.Value,
                DateTime = message.Metadata.Timestamp,
                FromUser = new UserInfoDto
                {
                    Id = message.Metadata.FromUserId
                    // 简化实现：暂时不处理用户详细信息
                },
                Extensions = new List<MessageExtensionDto>() // 简化实现：暂时不处理扩展数据
            };
        }
    }

}
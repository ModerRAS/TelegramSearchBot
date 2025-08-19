using MediatR;
using TelegramSearchBot.Application.Abstractions;
using TelegramSearchBot.Application.DTOs.Requests;
using TelegramSearchBot.Application.DTOs.Responses;

namespace TelegramSearchBot.Application.Features.Messages
{
    /// <summary>
    /// 创建消息命令
    /// </summary>
    public record CreateMessageCommand(MessageDto MessageDto, long GroupId) : IRequest<long>;

    /// <summary>
    /// 更新消息命令
    /// </summary>
    public record UpdateMessageCommand(long Id, MessageDto MessageDto, long GroupId) : IRequest;

    /// <summary>
    /// 删除消息命令
    /// </summary>
    public record DeleteMessageCommand(long Id, long GroupId) : IRequest;

    /// <summary>
    /// 根据ID获取消息查询
    /// </summary>
    public record GetMessageByIdQuery(long Id, long GroupId) : IRequest<MessageDto>;

    /// <summary>
    /// 根据群组获取消息查询
    /// </summary>
    public record GetMessagesByGroupQuery(long GroupId, int Skip = 0, int Take = 20) : IRequest<IEnumerable<MessageDto>>;

    /// <summary>
    /// 搜索消息查询
    /// </summary>
    public record SearchMessagesQuery(string Query, long? GroupId = null, int Skip = 0, int Take = 20) : IRequest<SearchResponseDto>;

    /// <summary>
    /// 消息应用服务接口
    /// </summary>
    public interface IMessageApplicationService : IApplicationService
    {
        Task<long> CreateMessageAsync(CreateMessageCommand command);
        Task UpdateMessageAsync(UpdateMessageCommand command);
        Task DeleteMessageAsync(DeleteMessageCommand command);
        Task<MessageDto> GetMessageByIdAsync(GetMessageByIdQuery query);
        Task<IEnumerable<MessageDto>> GetMessagesByGroupAsync(GetMessagesByGroupQuery query);
        Task<SearchResponseDto> SearchMessagesAsync(SearchMessagesQuery query);
    }
}
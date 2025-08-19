using MediatR;

namespace TelegramSearchBot.Application.Abstractions
{
    /// <summary>
    /// 应用服务基础接口
    /// </summary>
    public interface IApplicationService
    {
    }

    /// <summary>
    /// 请求处理器接口
    /// </summary>
    /// <typeparam name="TRequest">请求类型</typeparam>
    /// <typeparam name="TResponse">响应类型</typeparam>
    public interface IRequestHandler<in TRequest, TResponse> 
        : MediatR.IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
    }

    /// <summary>
    /// 通知处理器接口
    /// </summary>
    /// <typeparam name="TNotification">通知类型</typeparam>
    public interface INotificationHandler<in TNotification> 
        : MediatR.INotificationHandler<TNotification>
        where TNotification : INotification
    {
    }
}
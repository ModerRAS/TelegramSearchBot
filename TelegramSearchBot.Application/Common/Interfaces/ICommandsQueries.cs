using MediatR;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramSearchBot.Application.Common.Interfaces
{
    /// <summary>
    /// 通用查询接口
    /// </summary>
    /// <typeparam name="TResponse">响应类型</typeparam>
    public interface IQuery<TResponse> : IRequest<TResponse>
    {
    }

    /// <summary>
    /// 通用命令接口
    /// </summary>
    /// <typeparam name="TResponse">响应类型</typeparam>
    public interface ICommand<TResponse> : IRequest<TResponse>
    {
    }

    /// <summary>
    /// 无返回值的命令接口
    /// </summary>
    public interface ICommand : IRequest<Unit>
    {
    }
}
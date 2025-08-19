using System;
using Microsoft.Extensions.DependencyInjection;

namespace TelegramSearchBot.Core
{
    /// <summary>
    /// 服务工厂，用于解析依赖
    /// </summary>
    public class ServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public ServiceFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// 获取服务实例
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <returns>服务实例</returns>
        public T GetService<T>() where T : notnull
        {
            return _serviceProvider.GetRequiredService<T>();
        }

        /// <summary>
        /// 尝试获取服务实例
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <returns>服务实例，如果不存在则返回null</returns>
        public T? GetOptionalService<T>() where T : notnull
        {
            return _serviceProvider.GetService<T>();
        }

        /// <summary>
        /// 创建作用域并执行操作
        /// </summary>
        /// <typeparam name="T">返回类型</typeparam>
        /// <param name="action">要执行的操作</param>
        /// <returns>操作结果</returns>
        public T ExecuteInScope<T>(Func<IServiceProvider, T> action)
        {
            using var scope = _serviceProvider.CreateScope();
            return action(scope.ServiceProvider);
        }

        /// <summary>
        /// 创建作用域并执行异步操作
        /// </summary>
        /// <typeparam name="T">返回类型</typeparam>
        /// <param name="action">要执行的异步操作</param>
        /// <returns>操作结果</returns>
        public async Task<T> ExecuteInScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
        {
            using var scope = _serviceProvider.CreateScope();
            return await action(scope.ServiceProvider);
        }

        /// <summary>
        /// 创建作用域并执行操作（无返回值）
        /// </summary>
        /// <param name="action">要执行的操作</param>
        public void ExecuteInScope(Action<IServiceProvider> action)
        {
            using var scope = _serviceProvider.CreateScope();
            action(scope.ServiceProvider);
        }

        /// <summary>
        /// 创建作用域并执行异步操作（无返回值）
        /// </summary>
        /// <param name="action">要执行的异步操作</param>
        public async Task ExecuteInScopeAsync(Func<IServiceProvider, Task> action)
        {
            using var scope = _serviceProvider.CreateScope();
            await action(scope.ServiceProvider);
        }
    }
}
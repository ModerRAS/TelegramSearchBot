using System;
using Microsoft.Extensions.DependencyInjection;

namespace TelegramSearchBot.Core.Attributes {
    /// <summary>
    /// 标记需要自动注入到DI容器的类
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class InjectableAttribute : Attribute {
        /// <summary>
        /// 服务生命周期，默认为Transient
        /// </summary>
        public ServiceLifetime Lifetime { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="lifetime">服务生命周期</param>
        public InjectableAttribute(ServiceLifetime lifetime = ServiceLifetime.Transient) {
            Lifetime = lifetime;
        }
    }
}

using System;
using System.Threading.Tasks;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Extension;
using TelegramSearchBot.Interface.AI.QR;
using TelegramSearchBot.Service.Abstract;

namespace TelegramSearchBot.Service.AI.QR {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class AutoQRService : SubProcessService, IAutoQRService {
        public AutoQRService(IConnectionMultiplexer connectionMultiplexer) : base(connectionMultiplexer) {
            ForkName = "QR";
        }

        public new string ServiceName => "AutoQRService";


        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public async Task<string> ExecuteAsync(string path) {
            return await RunRpc(path);
        }
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
    }
}

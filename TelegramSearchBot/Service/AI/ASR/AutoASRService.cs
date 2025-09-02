using System.Threading.Tasks;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface.AI.ASR;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Service.Abstract;

namespace TelegramSearchBot.Service.AI.ASR {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class AutoASRService : SubProcessService, IAutoASRService {


        public new string ServiceName => "AutoASRService";
        public WhisperManager WhisperManager { get; set; }

        public AutoASRService(IConnectionMultiplexer connectionMultiplexer) : base(connectionMultiplexer) {
            ForkName = "ASR";
        }

        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(string path) {
            return await RunRpc(path);
        }
    }
}

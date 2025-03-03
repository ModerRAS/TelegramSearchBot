using StackExchange.Redis;
using System.Threading.Tasks;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Service {
    public class AutoASRService : SubProcessService {
        

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

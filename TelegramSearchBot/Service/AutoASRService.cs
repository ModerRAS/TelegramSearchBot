using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;

namespace TelegramSearchBot.Service {
    public class AutoASRService : IService {
        public string ServiceName => "AutoASRService";
        public WhisperManager WhisperManager { get; set; }

        public AutoASRService(WhisperManager WhisperManager) {
            this.WhisperManager = WhisperManager;
        }


        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(Stream file) {
            if (!Env.EnableAutoASR) {
                return "";
            }
            return await WhisperManager.ExecuteAsync(file);
        }
    }
}

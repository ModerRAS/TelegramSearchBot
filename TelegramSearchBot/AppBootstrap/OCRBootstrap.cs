using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;
using TelegramSearchBot.AI.OCR;

namespace TelegramSearchBot.AppBootstrap {
    public class OCRBootstrap : AppBootstrap {
        public static void Startup(string[] args) {
            // 转发到AI.OCR项目的OCRBootstrap
            TelegramSearchBot.AI.OCR.OCRBootstrap.Startup(args);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;
using TelegramSearchBot.OCR;  // 引用新的OCR库

namespace TelegramSearchBot.AppBootstrap {
    public class OCRBootstrap : AppBootstrap {
        public static void Startup(string[] args) {
            // 直接调用新的OCR库中的OCRBootstrap
            TelegramSearchBot.OCR.OCRBootstrap.Startup(args);
        }
    }
}
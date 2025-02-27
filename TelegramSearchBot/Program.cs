using LiteDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.AppBootstrap;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Executor;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service;

namespace TelegramSearchBot {
    class Program {
        static void Main(string[] args) {
            if (args.Length == 0) {
                GeneralBootstrap.Startup(args);
            } else if (args.Length == 2) {
                if (args[0].Equals("ASR")) {
                    ASRBootstrap.Startup(args); 
                } else if (args[0].Equals("OCR")) {
                    OCRBootstrap.Startup(args);
                } else if (args[0].Equals("Scheduler")) {
                    SchedulerBootstrap.Startup(args);
                }
            }
        }
    }
}

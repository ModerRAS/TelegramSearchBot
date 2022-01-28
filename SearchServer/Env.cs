using LiteDB;
using System;
using System.Collections.Generic;
using System.Text;

namespace SearchServer {
    class Env {
        public static readonly string WorkDir = Environment.GetEnvironmentVariable("WorkDir") ?? "/data/TelegramSearchBot";
        public static LiteDatabase Database { get; set; }
        public static LiteDatabase Cache { get; set; }
    }
}

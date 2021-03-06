﻿using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot {
    class Env {
        public static readonly string HttpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("http_proxy") ?? Environment.GetEnvironmentVariable("https_proxy") ?? string.Empty;
        public static readonly string BotToken = Environment.GetEnvironmentVariable("BotToken") ?? string.Empty;
        public static readonly string DatabaseHost = Environment.GetEnvironmentVariable("Host") ?? string.Empty;
        public static readonly string Database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? string.Empty;
        public static readonly string Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? string.Empty;
        public static readonly string Password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? string.Empty;
        public static readonly string RedisConnString = Environment.GetEnvironmentVariable("RedisConnString") ?? string.Empty;
        public static readonly long AdminId = long.Parse(Environment.GetEnvironmentVariable("AdminId") ?? string.Empty);
        public static readonly string SonicHostname = Environment.GetEnvironmentVariable("SonicHostname") ?? string.Empty;
        public static readonly int SonicPort = int.Parse(Environment.GetEnvironmentVariable("SonicPort") ?? string.Empty);
        public static readonly string SonicSecret = Environment.GetEnvironmentVariable("SonicSecret") ?? string.Empty;
        public static readonly string SonicCollection = Environment.GetEnvironmentVariable("SonicCollection") ?? string.Empty;
        public static readonly bool EnableAutoOCR = (Environment.GetEnvironmentVariable("EnableAutoOCR") ?? string.Empty).Equals("true");
    }
}

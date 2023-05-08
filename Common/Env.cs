using System;
using System.Collections.Generic;
using System.Text;

namespace TelegramSearchBot.Common {
    public class Env {
        public static readonly string AgileConfigAppId = Environment.GetEnvironmentVariable("AgileConfigAppId") ?? string.Empty;
        public static readonly string AgileConfigSecret = Environment.GetEnvironmentVariable("AgileConfigSecret") ?? string.Empty;
        public static readonly string AgileConfigServerNodes = Environment.GetEnvironmentVariable("AgileConfigServerNodes") ?? string.Empty;
        public static readonly string AgileConfigEnv = Environment.GetEnvironmentVariable("AgileConfigEnv") ?? string.Empty;
    }
}

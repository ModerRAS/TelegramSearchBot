using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Serilog;

namespace TelegramSearchBot.AppBootstrap {
    public class LLMAgentBootstrap : AppBootstrap {
        public static void Startup(string[] args) {
            try {
                var effectiveArgs = args.Length > 0 && args[0].Equals("LLMAgent", StringComparison.OrdinalIgnoreCase)
                    ? args.Skip(1).ToArray()
                    : args;
                var dllPath = Path.Combine(AppContext.BaseDirectory, "TelegramSearchBot.LLMAgent.dll");
                if (!File.Exists(dllPath)) {
                    throw new FileNotFoundException("LLMAgent executable not found.", dllPath);
                }

                using var process = Process.Start(new ProcessStartInfo {
                    FileName = "dotnet",
                    Arguments = $"\"{dllPath}\" {string.Join(" ", effectiveArgs)}",
                    UseShellExecute = false
                });
                process?.WaitForExit();
                Environment.ExitCode = process?.ExitCode ?? 1;
            } catch (Exception ex) {
                Log.Error(ex, "LLMAgent startup failed.");
                Environment.ExitCode = 1;
            }
        }
    }
}

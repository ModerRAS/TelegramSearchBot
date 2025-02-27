using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.AppBootstrap {
    public class AppBootstrap {
        public static Task Fork(string[] args) {
            // 获取当前进程的文件路径
            string exePath = Environment.ProcessPath;//System.Reflection.Assembly.GetExecutingAssembly().Location;

            // 将参数数组转换为空格分隔的字符串，并正确处理包含空格的参数
            string arguments = string.Join(" ", args.Select(arg => $"{arg}"));

            // 启动新的进程（自己）
            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = exePath,  // 启动自身
                Arguments = arguments,  // 传递参数
                UseShellExecute = false,   // 确保进程能够继承环境变量
                RedirectStandardOutput = true,  // 可选：如果需要获取新进程的输出
                RedirectStandardError = true   // 可选：如果需要获取错误输出
            };

            Process newProcess = Process.Start(startInfo); // 启动新进程
            Log.Logger.Information($"主进程：{args[0]}已启动");

            return newProcess.WaitForExitAsync(); // 异步等待进程退出
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramSearchBot.AppBootstrap {
    public class DaemonBootstrap {
        public static void Startup(string[] args) {
            // 从参数中获取子进程的 PID
            int childProcessId = int.Parse(args[0]);

            while (true) {
                try {
                    // 获取子进程
                    Process childProcess = Process.GetProcessById(childProcessId);

                    // 检查子进程是否退出
                    if (childProcess.HasExited) {
                        Console.WriteLine("子进程已退出，重新启动...");
                        // 重新启动子进程
                        Process.Start("child_process.exe");
                    }
                } catch (ArgumentException) {
                    Console.WriteLine("子进程不存在，重新启动...");
                    // 重新启动子进程
                    Process.Start("child_process.exe");
                } catch (Exception ex) {
                    Console.WriteLine($"守护进程发生错误：{ex.Message}");
                }

                // 休眠一段时间
                Thread.Sleep(5000);
            }
        }
    }
}

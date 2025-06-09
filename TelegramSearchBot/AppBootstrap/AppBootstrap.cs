using Microsoft.Win32.SafeHandles;
using Nito.AsyncEx;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramSearchBot.AppBootstrap {
    public class AppBootstrap {
        public sealed class ChildProcessManager : IDisposable {
            private SafeJobHandle? _handle;
            private bool _disposed;

            public ChildProcessManager() {
                _handle = new SafeJobHandle(CreateJobObject(IntPtr.Zero, null));

                var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION {
                    LimitFlags = 0x2000
                };

                var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
                    BasicLimitInformation = info
                };

                var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                var extendedInfoPtr = Marshal.AllocHGlobal(length);
                Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

                if (!SetInformationJobObject(_handle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length)) {
                    throw new InvalidOperationException("Unable to set information", new Win32Exception());
                }
            }

            public void Dispose() {
                if (_disposed) return;

                _handle?.Dispose();
                _handle = null;
                _disposed = true;
            }

            [MemberNotNull(nameof(_handle))]
            private void ValidateDisposed() {
                ObjectDisposedException.ThrowIf(_disposed || _handle is null, this);
            }

            public void AddProcess(SafeProcessHandle processHandle) {
                ValidateDisposed();
                if (!AssignProcessToJobObject(_handle, processHandle)) {
                    throw new InvalidOperationException("Unable to add the process");
                }
            }

            public void AddProcess(Process process) {
                AddProcess(process.SafeHandle);
            }

            public void AddProcess(int processId) {
                using var process = Process.GetProcessById(processId);
                AddProcess(process);
            }

            private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid {
                public SafeJobHandle(IntPtr handle) : base(true) {
                    SetHandle(handle);
                }

                protected override bool ReleaseHandle() {
                    return CloseHandle(handle);
                }

                [DllImport("kernel32", SetLastError = true)]
                private static extern bool CloseHandle(IntPtr hObject);
            }

            [DllImport("kernel32", CharSet = CharSet.Unicode)]
            private static extern IntPtr CreateJobObject(IntPtr a, string? lpName);

            [DllImport("kernel32")]
            private static extern bool SetInformationJobObject(SafeJobHandle hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

            [DllImport("kernel32", SetLastError = true)]
            private static extern bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

            [StructLayout(LayoutKind.Sequential)]
            private struct IO_COUNTERS {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_BASIC_LIMIT_INFORMATION {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct SECURITY_ATTRIBUTES {
                public uint nLength;
                public IntPtr lpSecurityDescriptor;
                public int bInheritHandle;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }

            private enum JobObjectInfoType {
                AssociateCompletionPortInformation = 7,
                BasicLimitInformation = 2,
                BasicUIRestrictions = 4,
                EndOfJobTimeInformation = 6,
                ExtendedLimitInformation = 9,
                SecurityLimitInformation = 5,
                GroupInformation = 11
            }
        }

        public static ChildProcessManager childProcessManager = new ChildProcessManager();
        public static void Fork(string[] args) {
            string exePath = Environment.ProcessPath;

            // 将参数数组转换为空格分隔的字符串，并正确处理包含空格的参数
            string arguments = string.Join(" ", args.Select(arg => $"{arg}"));

            // 启动新的进程（自己）
            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var newProcess = Process.Start(startInfo);
            if (newProcess == null) {
                throw new Exception("启动新进程失败");
            }
            childProcessManager.AddProcess(newProcess);
            Log.Logger.Information($"主进程：{args[0]} {args[1]}已启动");
        }
        private static Dictionary<string, DateTime> ForkLock = new Dictionary<string, DateTime>();
        private static readonly AsyncLock _asyncLock = new AsyncLock();
        public static async Task RateLimitForkAsync(string[] args) {
            using (await _asyncLock.LockAsync()) {
                if (ForkLock.ContainsKey(args[0])) {
                    if (DateTime.UtcNow - ForkLock[args[0]] > TimeSpan.FromMinutes(5)) {
                        Fork(args);
                        ForkLock[args[0]] = DateTime.UtcNow;
                    }
                } else {
                    Fork(args);
                    ForkLock.Add(args[0],DateTime.UtcNow);
                }
            }
        }

        public static Process Fork(string exePath, string[] args) {
            // 将参数数组转换为空格分隔的字符串，并正确处理包含空格的参数
            string arguments = string.Join(" ", args.Select(arg => $"{arg}"));

            // 启动新的进程（自己）
            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var newProcess = Process.Start(startInfo);
            if (newProcess == null) {
                throw new Exception("启动新进程失败");
            }
            childProcessManager.AddProcess(newProcess);
            Log.Logger.Information($"进程：{exePath} {string.Join(" ", args)}已启动");
            return newProcess;
        }
        public static async Task RateLimitForkAsync(string exePath, string[] args) {
            using (await _asyncLock.LockAsync()) {
                if (ForkLock.ContainsKey(exePath)) {
                    if (DateTime.UtcNow - ForkLock[args[0]] > TimeSpan.FromMinutes(5)) {
                        Fork(exePath, args);
                        ForkLock[exePath] = DateTime.UtcNow;
                    }
                } else {
                    Fork(exePath, args);
                    ForkLock.Add(exePath, DateTime.UtcNow);
                }
            }
        }

        // 定义 Bootstrap 类的后缀 (命名约定)
        private const string BootstrapSuffix = "Bootstrap";
        // 定义要调用的静态方法名 (命名约定)
        private const string StartupMethodName = "Startup";

        private const string TargetNamespace = "TelegramSearchBot.AppBootstrap";

        /// <summary>
        /// 尝试根据第一个命令行参数通过反射查找并执行相应的 Bootstrap 类的 Startup 方法。
        /// </summary>
        /// <param name="args">命令行参数数组，期望 args[0] 是启动类型关键字。</param>
        /// <returns>如果成功找到并调用了 Startup 方法，则返回 true；否则返回 false。</returns>
        public static bool TryDispatchStartupByReflection(string[] args) {
            if (args == null || args.Length == 0) {
                Log.Error("错误：缺少启动参数。");
                return false;
            }

            string startupKey = args[0];
            // 1. 根据命名约定构造目标类型名称
            string targetTypeName = startupKey + BootstrapSuffix;
            Assembly currentAssembly = Assembly.GetExecutingAssembly(); // 获取当前程序集一次即可

            try {
                // 2. 在程序集中查找符合名称约定的类型 (忽略大小写)
                Type bootstrapType = currentAssembly.GetTypes().FirstOrDefault(t =>
                    t.IsClass &&
                    string.Equals(t.Namespace, TargetNamespace, StringComparison.Ordinal) &&
                    t.Name.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase));

                if (bootstrapType != null) {
                    // 3. 查找目标类型中名为 StartupMethodName 的公共静态方法
                    MethodInfo startupMethod = bootstrapType.GetMethod(
                        StartupMethodName,
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        new Type[] { typeof(string[]) },
                        null
                    );

                    if (startupMethod != null) {
                        // 4. 动态调用该静态方法
                        startupMethod.Invoke(null, new object[] { args });
                        return true; // 调用成功
                    } else {
                        // 在找到的类中未找到合适的 Startup 方法
                        Log.Error($"错误：在类型 '{bootstrapType.FullName}' 中未找到有效的静态方法 '{StartupMethodName}(string[])'");
                        PrintUsageHint(currentAssembly);
                        return false; // 方法未找到
                    }
                } else {
                    // 在程序集中未找到匹配的类型
                    Log.Error($"在命名空间 '{TargetNamespace}' 下未找到启动类型对应的类: '{targetTypeName}'");
                    PrintUsageHint(currentAssembly);
                    return false; // 类型未找到
                }
            } catch (TargetInvocationException ex) {
                // 被调用的 Startup 方法内部抛出了异常
                Log.Error($"错误：启动过程 '{startupKey}' 中发生异常: {ex.InnerException?.Message ?? ex.Message}");
                // 可以考虑记录更详细的堆栈信息 ex.InnerException.StackTrace
                return false; // 目标方法执行失败
            } catch (Exception ex) {
                // 其他反射或运行时错误
                Log.Error($"处理启动类型 '{startupKey}' 时发生意外错误: {ex.Message}");
                return false; // 反射或其他错误
            }
        }

        /// <summary>
        /// (可选) 打印基于命名约定找到的可能启动类型的提示。
        /// </summary>
        /// <param name="assembly">要搜索的程序集。</param>
        private static void PrintUsageHint(Assembly assembly) {
            Log.Information("\n可能的启动类型 (基于命名约定 *Bootstrap 类):");
            try {
                // 在传入的程序集中查找所有符合命名约定且包含 Startup(string[]) 方法的类
                var possibleTypes = assembly.GetTypes()
                    .Where(t => t.IsClass
                                && string.Equals(t.Namespace, TargetNamespace, StringComparison.Ordinal)
                                && t.Name.EndsWith(BootstrapSuffix, StringComparison.OrdinalIgnoreCase)
                                && t.GetMethod(StartupMethodName, BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(string[]) }, null) != null)
                    .Select(t => t.Name.Substring(0, t.Name.Length - BootstrapSuffix.Length))
                    .OrderBy(name => name); // 按字母顺序排序

                if (possibleTypes.Any()) {
                    foreach (var typeName in possibleTypes) {
                        Log.Information($"  - {typeName}");
                    }
                } else {
                    Log.Error("  (未找到符合约定的启动类型)");
                }

            } catch (Exception ex) {
                Log.Error($"  (无法自动列出类型: {ex.Message})");
            }
        }
    }
}


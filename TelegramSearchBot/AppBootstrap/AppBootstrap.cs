using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Nito.AsyncEx;
using Serilog;

namespace TelegramSearchBot.AppBootstrap {
    public class AppBootstrap {
        public sealed class ChildProcessManager : IDisposable {
            private readonly List<SafeJobHandle> _handles = [];
            private readonly List<Process> _processes = [];
            private bool _disposed;

            public void Dispose() {
                if (_disposed) return;

                foreach (var process in _processes) {
                    process.Dispose();
                }
                _processes.Clear();

                foreach (var handle in _handles) {
                    handle.Dispose();
                }
                _handles.Clear();
                _disposed = true;
            }

            private void ValidateDisposed() {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            public void AddProcess(SafeProcessHandle processHandle, long? processMemoryLimitBytes = null) {
                ValidateDisposed();
                var jobHandle = CreateConfiguredJobHandle(processMemoryLimitBytes);
                if (!AssignProcessToJobObject(jobHandle, processHandle)) {
                    jobHandle.Dispose();
                    throw new InvalidOperationException("Unable to add the process");
                }
                _handles.Add(jobHandle);
            }

            public void AddProcess(Process process, long? processMemoryLimitBytes = null) {
                ValidateDisposed();
                AddProcess(process.SafeHandle, processMemoryLimitBytes);
                _processes.Add(process);
            }

            public void AddProcess(int processId, long? processMemoryLimitBytes = null) {
                var process = Process.GetProcessById(processId);
                AddProcess(process, processMemoryLimitBytes);
            }

            private static SafeJobHandle CreateConfiguredJobHandle(long? processMemoryLimitBytes) {
                var handle = new SafeJobHandle(CreateJobObject(IntPtr.Zero, null));
                if (handle.IsInvalid) {
                    throw new InvalidOperationException("Unable to create job object", new Win32Exception());
                }

                var limitFlags = JobObjectLimitFlags.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                if (processMemoryLimitBytes.HasValue && processMemoryLimitBytes.Value > 0) {
                    limitFlags |= JobObjectLimitFlags.JOB_OBJECT_LIMIT_PROCESS_MEMORY;
                }

                var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION {
                    LimitFlags = (uint)limitFlags
                };

                var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
                    BasicLimitInformation = info,
                    ProcessMemoryLimit = processMemoryLimitBytes.HasValue && processMemoryLimitBytes.Value > 0
                        ? (UIntPtr)processMemoryLimitBytes.Value
                        : UIntPtr.Zero
                };

                var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                var extendedInfoPtr = Marshal.AllocHGlobal(length);
                try {
                    Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
                    if (!SetInformationJobObject(handle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length)) {
                        throw new InvalidOperationException("Unable to set information", new Win32Exception());
                    }
                } finally {
                    Marshal.FreeHGlobal(extendedInfoPtr);
                }

                return handle;
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

            [Flags]
            private enum JobObjectLimitFlags : uint {
                JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100,
                JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
            }
        }

        public static ChildProcessManager childProcessManager = new ChildProcessManager();
        public static Process Fork(string[] args, long? processMemoryLimitBytes = null) {
            string exePath = Environment.ProcessPath;
            var startInfo = CreateManagedStartInfo(exePath, args, Environment.CurrentDirectory);
            return StartManagedProcess(startInfo, $"主进程：{string.Join(" ", args)}", processMemoryLimitBytes);
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
                    ForkLock.Add(args[0], DateTime.UtcNow);
                }
            }
        }

        public static Process Fork(string exePath, string[] args, long? processMemoryLimitBytes = null) {
            var workingDirectory = Path.GetDirectoryName(exePath);
            var startInfo = CreateManagedStartInfo(exePath, args, string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory);
            return StartManagedProcess(startInfo, $"进程：{exePath} {string.Join(" ", args)}", processMemoryLimitBytes);
        }
        public static async Task RateLimitForkAsync(string exePath, string[] args) {
            using (await _asyncLock.LockAsync()) {
                if (ForkLock.ContainsKey(exePath)) {
                    if (DateTime.UtcNow - ForkLock[exePath] > TimeSpan.FromMinutes(5)) {
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

        private static ProcessStartInfo CreateManagedStartInfo(string exePath, IEnumerable<string> args, string workingDirectory) {
            var startInfo = new ProcessStartInfo {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            foreach (var arg in args) {
                startInfo.ArgumentList.Add(arg);
            }

            return startInfo;
        }

        private static Process StartManagedProcess(ProcessStartInfo startInfo, string processDisplayName, long? processMemoryLimitBytes) {
            var process = new Process {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) => {
                if (!string.IsNullOrWhiteSpace(e.Data)) {
                    Log.Logger.Information("[{Process}] {Message}", processDisplayName, e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) => {
                if (!string.IsNullOrWhiteSpace(e.Data)) {
                    Log.Logger.Warning("[{Process}] {Message}", processDisplayName, e.Data);
                }
            };
            process.Exited += (_, _) => {
                try {
                    Log.Logger.Information("[{Process}] exited with code {ExitCode}", processDisplayName, process.ExitCode);
                } catch {
                }
            };

            if (!process.Start()) {
                throw new Exception("启动新进程失败");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            childProcessManager.AddProcess(process, processMemoryLimitBytes);
            Log.Logger.Information("{Process}已启动", processDisplayName);
            return process;
        }

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


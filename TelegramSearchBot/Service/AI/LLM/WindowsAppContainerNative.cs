#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace TelegramSearchBot.Service.AI.LLM;

[SupportedOSPlatform("windows")]
internal static class WindowsAppContainerNative {
    private static readonly IReadOnlyDictionary<string, string> CapabilitySidValues =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["internetClient"] = "S-1-15-3-1",
            ["privateNetworkClientServer"] = "S-1-15-3-3"
        };

    private const uint ErrorAlreadyExists = 183;
    private const uint SeGroupEnabled = 0x00000004;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateSuspended = 0x00000004;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint JobObjectLimitActiveProcess = 0x00000008;
    private const uint JobObjectLimitJobMemory = 0x00000200;
    private const int ProcThreadAttributeSecurityCapabilities = 9 | 0x00020000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    internal static SecurityIdentifier EnsureProfile(string profileName, string displayName) {
        EnsureWindows();
        var hr = CreateAppContainerProfile(profileName, displayName, displayName, IntPtr.Zero, 0, out var sid);
        if (hr == HResultFromWin32(ErrorAlreadyExists)) {
            hr = DeriveAppContainerSidFromAppContainerName(profileName, out sid);
        }
        Marshal.ThrowExceptionForHR(hr);
        try {
            return new SecurityIdentifier(sid);
        } finally {
            FreeSid(sid);
        }
    }

    internal static void DeleteProfile(string profileName) {
        EnsureWindows();
        var hr = DeleteAppContainerProfile(profileName);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
    }

    internal static void GrantReadOnlyDirectory(string path, SecurityIdentifier sid) {
        if (!Directory.Exists(path)) return;
        GrantParentTraversal(path, sid);
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl(AccessControlSections.Access);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    internal static void GrantWritableDirectory(string path, SecurityIdentifier sid) {
        Directory.CreateDirectory(path);
        GrantParentTraversal(path, sid);
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl(AccessControlSections.Access);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.Modify | FileSystemRights.Synchronize,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(security);

        var result = SetLowIntegrityLabel(path);
        if (result != 0) {
            throw new Win32Exception(result, $"Failed to set Low integrity label on '{path}'.");
        }
    }

    internal static void GrantParentTraversal(string path, SecurityIdentifier sid) {
        var parent = Directory.GetParent(Path.GetFullPath(path));
        if (parent == null) return;
        var security = parent.GetAccessControl(AccessControlSections.Access);
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.Traverse,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        parent.SetAccessControl(security);
    }

    internal static void RemoveDirectoryRules(string path, SecurityIdentifier sid) {
        if (!Directory.Exists(path)) return;
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl(AccessControlSections.Access);
        security.PurgeAccessRules(sid);
        info.SetAccessControl(security);
    }

    internal static AppContainerProcess Start(
        SecurityIdentifier appContainerSid,
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyList<string> capabilityNames,
        int activeProcessLimit,
        long jobMemoryLimitBytes) {
        EnsureWindows();
        var sidBytes = new byte[appContainerSid.BinaryLength];
        appContainerSid.GetBinaryForm(sidBytes, 0);
        var sidPtr = Marshal.AllocHGlobal(sidBytes.Length);
        Marshal.Copy(sidBytes, 0, sidPtr, sidBytes.Length);

        var capabilitySids = new List<IntPtr>();
        IntPtr capabilitiesPtr = IntPtr.Zero;
        IntPtr securityCapabilitiesPtr = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        SafeJobHandle? job = null;
        try {
            foreach (var name in capabilityNames) {
                capabilitySids.Add(DeriveCapabilitySid(name));
            }

            if (capabilitySids.Count > 0) {
                var itemSize = Marshal.SizeOf<SidAndAttributes>();
                capabilitiesPtr = Marshal.AllocHGlobal(itemSize * capabilitySids.Count);
                for (var i = 0; i < capabilitySids.Count; i++) {
                    Marshal.StructureToPtr(new SidAndAttributes {
                        Sid = capabilitySids[i],
                        Attributes = SeGroupEnabled
                    }, capabilitiesPtr + i * itemSize, false);
                }
            }

            var capabilities = new SecurityCapabilities {
                AppContainerSid = sidPtr,
                Capabilities = capabilitiesPtr,
                CapabilityCount = capabilitySids.Count
            };
            securityCapabilitiesPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityCapabilities>());
            Marshal.StructureToPtr(capabilities, securityCapabilitiesPtr, false);

            nuint attributeListSize = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
            attributeList = Marshal.AllocHGlobal(checked((int)attributeListSize));
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize)) {
                throw new Win32Exception();
            }
            if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    (IntPtr)ProcThreadAttributeSecurityCapabilities,
                    securityCapabilitiesPtr,
                    (nuint)Marshal.SizeOf<SecurityCapabilities>(),
                    IntPtr.Zero,
                    IntPtr.Zero)) {
                throw new Win32Exception();
            }

            job = CreateConfiguredJob(activeProcessLimit, jobMemoryLimitBytes);
            var startupInfo = new StartupInfoEx {
                StartupInfo = new StartupInfo { Cb = Marshal.SizeOf<StartupInfoEx>() },
                AttributeList = attributeList
            };
            var commandLine = new System.Text.StringBuilder(BuildCommandLine(executable, arguments));
            var environment = BuildEnvironmentBlock(workingDirectory);
            try {
                if (!CreateProcessW(
                        executable,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        ExtendedStartupInfoPresent | CreateUnicodeEnvironment | CreateSuspended,
                        environment,
                        workingDirectory,
                        ref startupInfo,
                        out var processInfo)) {
                    throw new Win32Exception();
                }

                var processHandle = new SafeProcessHandle(processInfo.Process, true);
                var threadHandle = new SafeWaitHandle(processInfo.Thread, true);
                try {
                    if (!AssignProcessToJobObject(job, processHandle)) {
                        throw new Win32Exception();
                    }
                    if (ResumeThread(threadHandle) == uint.MaxValue) {
                        throw new Win32Exception();
                    }
                    return new AppContainerProcess(Process.GetProcessById(processInfo.ProcessId), processHandle, threadHandle, job);
                } catch {
                    TerminateProcess(processHandle, 1);
                    processHandle.Dispose();
                    threadHandle.Dispose();
                    throw;
                }
            } finally {
                Marshal.FreeHGlobal(environment);
            }
        } catch {
            job?.Dispose();
            throw;
        } finally {
            if (attributeList != IntPtr.Zero) {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
            if (securityCapabilitiesPtr != IntPtr.Zero) Marshal.FreeHGlobal(securityCapabilitiesPtr);
            if (capabilitiesPtr != IntPtr.Zero) Marshal.FreeHGlobal(capabilitiesPtr);
            foreach (var capabilitySid in capabilitySids) Marshal.FreeHGlobal(capabilitySid);
            Marshal.FreeHGlobal(sidPtr);
        }
    }

    internal static bool HasLoopbackExemption(SecurityIdentifier sid) {
        var output = RunCheckNetIsolation("LoopbackExempt", "-s");
        return output.ExitCode == 0 && output.StandardOutput.Contains(sid.Value, StringComparison.OrdinalIgnoreCase);
    }

    internal static void EnsureLoopbackExemption(SecurityIdentifier sid) {
        if (HasLoopbackExemption(sid)) return;
        var result = RunCheckNetIsolation("LoopbackExempt", "-a", $"-p={sid.Value}");
        if (result.ExitCode != 0) {
            throw new InvalidOperationException(
                $"AppContainer loopback exemption is required for Redis. Run elevated: CheckNetIsolation.exe LoopbackExempt -a -p={sid.Value}. " +
                result.StandardError.Trim());
        }
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunCheckNetIsolation(params string[] arguments) {
        var startInfo = new ProcessStartInfo {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "CheckNetIsolation.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start CheckNetIsolation.exe.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static SafeJobHandle CreateConfiguredJob(int activeProcessLimit, long jobMemoryLimitBytes) {
        var job = new SafeJobHandle(CreateJobObjectW(IntPtr.Zero, null), true);
        if (job.IsInvalid) throw new Win32Exception();

        var info = new JobObjectExtendedLimitInformation {
            BasicLimitInformation = new JobObjectBasicLimitInformation {
                LimitFlags = JobObjectLimitKillOnJobClose | JobObjectLimitActiveProcess | JobObjectLimitJobMemory,
                ActiveProcessLimit = (uint)Math.Max(1, activeProcessLimit)
            },
            JobMemoryLimit = (nuint)Math.Max(64L * 1024 * 1024, jobMemoryLimitBytes)
        };
        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var ptr = Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, ptr, (uint)size)) {
                throw new Win32Exception();
            }
        } catch {
            job.Dispose();
            throw;
        } finally {
            Marshal.FreeHGlobal(ptr);
        }
        return job;
    }

    private static IntPtr DeriveCapabilitySid(string name) {
        if (!CapabilitySidValues.TryGetValue(name, out var sidValue)) {
            throw new InvalidOperationException($"Unsupported AppContainer capability '{name}'.");
        }
        var sid = new SecurityIdentifier(sidValue);
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    private static string BuildCommandLine(string executable, IReadOnlyList<string> arguments) {
        return string.Join(' ', new[] { QuoteArgument(executable) }.Concat(arguments.Select(QuoteArgument)));
    }

    internal static string QuoteArgument(string argument) {
        if (argument.Length > 0 && !argument.Any(char.IsWhiteSpace) && !argument.Contains('"')) return argument;
        var result = new System.Text.StringBuilder(argument.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var c in argument) {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') result.Append('\\', backslashes * 2 + 1).Append('"');
            else result.Append('\\', backslashes).Append(c);
            backslashes = 0;
        }
        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private static IntPtr BuildEnvironmentBlock(string workingDirectory) {
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["ALLUSERSPROFILE"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["APPDATA"] = workingDirectory,
            ["COMSPEC"] = Environment.GetEnvironmentVariable("COMSPEC") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["HOME"] = workingDirectory,
            ["LOCALAPPDATA"] = workingDirectory,
            ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            ["PATHEXT"] = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD",
            ["PROGRAMDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["PROGRAMFILES"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ["PROGRAMFILES(X86)"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            ["PROGRAMW6432"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ["SYSTEMDRIVE"] = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd(Path.DirectorySeparatorChar) ?? "C:",
            ["SYSTEMROOT"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ["TEMP"] = workingDirectory,
            ["TMP"] = workingDirectory,
            ["USERNAME"] = Environment.UserName,
            ["USERPROFILE"] = workingDirectory,
            ["WINDIR"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };
        var text = string.Join('\0', values.Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0";
        var bytes = Encoding.Unicode.GetBytes(text);
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        return buffer;
    }

    private static int SetLowIntegrityLabel(string path) {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("S:(ML;OICI;NW;;;LW)", 1, out var descriptor, out _)) {
            return Marshal.GetLastWin32Error();
        }
        try {
            var saclPresent = false;
            var saclDefaulted = false;
            if (!GetSecurityDescriptorSacl(descriptor, out saclPresent, out var sacl, out saclDefaulted) || !saclPresent) {
                return Marshal.GetLastWin32Error();
            }
            return (int)SetNamedSecurityInfoW(path, 1, 0x00000010, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, sacl);
        } finally {
            LocalFree(descriptor);
        }
    }

    private static void EnsureWindows() {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows AppContainer sandbox is only available on Windows.");
    }

    private static int HResultFromWin32(uint error) => error <= 0 ? (int)error : unchecked((int)(0x80070000u | error));

    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityCapabilities { public IntPtr AppContainerSid; public IntPtr Capabilities; public int CapabilityCount; public int Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo { public int Cb; public string? Reserved; public string? Desktop; public string? Title; public uint X; public uint Y; public uint XSize; public uint YSize; public uint XCountChars; public uint YCountChars; public uint FillAttribute; public uint Flags; public ushort ShowWindow; public ushort Reserved2; public IntPtr Reserved2Ptr; public IntPtr StdInput; public IntPtr StdOutput; public IntPtr StdError; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr AttributeList; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process; public IntPtr Thread; public int ProcessId; public int ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount; public ulong WriteOperationCount; public ulong OtherOperationCount; public ulong ReadTransferCount; public ulong WriteTransferCount; public ulong OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct JobObjectBasicLimitInformation { public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags; public nuint MinimumWorkingSetSize; public nuint MaximumWorkingSetSize; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass; public uint SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct JobObjectExtendedLimitInformation { public JobObjectBasicLimitInformation BasicLimitInformation; public IoCounters IoInfo; public nuint ProcessMemoryLimit; public nuint JobMemoryLimit; public nuint PeakProcessMemoryUsed; public nuint PeakJobMemoryUsed; }

    internal sealed class AppContainerProcess : IDisposable {
        private readonly SafeProcessHandle _processHandle;
        private readonly SafeWaitHandle _threadHandle;
        private readonly SafeJobHandle _jobHandle;
        internal AppContainerProcess(Process process, SafeProcessHandle processHandle, SafeWaitHandle threadHandle, SafeJobHandle jobHandle) { Process = process; _processHandle = processHandle; _threadHandle = threadHandle; _jobHandle = jobHandle; }
        internal Process Process { get; }
        public void Dispose() { Process.Dispose(); _threadHandle.Dispose(); _processHandle.Dispose(); _jobHandle.Dispose(); }
    }

    internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid {
        internal SafeJobHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle) => SetHandle(handle);
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] private static extern int CreateAppContainerProfile(string name, string displayName, string description, IntPtr capabilities, uint capabilityCount, out IntPtr sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] private static extern int DeleteAppContainerProfile(string name);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string descriptor, uint revision, out IntPtr securityDescriptor, out uint size);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetSecurityDescriptorSacl(IntPtr securityDescriptor, out bool saclPresent, out IntPtr sacl, out bool saclDefaulted);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint SetNamedSecurityInfoW(string objectName, int objectType, uint securityInfo, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("advapi32.dll")] private static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, int flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr attributeList, uint flags, IntPtr attribute, IntPtr value, nuint size, IntPtr previousValue, IntPtr returnSize);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessW(string applicationName, System.Text.StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfoEx startupInfo, out ProcessInformation processInformation);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeJobHandle job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeWaitHandle thread);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}

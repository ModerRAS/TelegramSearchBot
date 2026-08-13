# Windows 原生进程 Sandbox 可行性研究

> 研究目标：允许 LLM 执行任意 PowerShell、CMD、Git Bash、脚本和子进程，但由 Windows 内核安全边界把它们限制在每个 Task 被授权的资源内。
>
> 研究日期：2026-08-11。目标平台应明确限定为受支持的 Windows 11 x64/NTFS；不要把结论外推到 FAT/exFAT、旧版 Windows 或 Wine。

### 1.0 第一阶段目标

第一阶段只解决一个核心问题：**限制 Sandbox 内程序访问宿主文件目录。**

保留现有 Redis/Garnet、心跳、任务队列和网络行为，不在这一阶段更换 IPC 或设计 WFP 策略。实施内容是：

1. ToolHost 运行在普通 AppContainer 中。
2. 给该 AppContainer SID 只授权指定 workspace 和必要输入目录。
3. workspace 使用 NTFS DACL 授予该 SID `Modify`，并设置 Low Integrity 可写标签。
4. Bot 的 `Config.json`、`Data.sqlite`、日志、用户凭据目录和其他 workspace 不授予该 SID。
5. ToolHost 启动的 CMD、PowerShell、Git、Python、Node 等正常子进程继承同一个 AppContainer token，因此接受同一套目录权限。
6. Windows、PowerShell、.NET 和其他运行时所需的系统文件保持只读可执行；不能把“只允许 workspace”理解为连系统 DLL 都禁止读取。

第一阶段的验收标准是：允许目录能够正常读写，未授权的数据目录返回 `Access denied`。网络暂不作为安全边界，Redis 只需保持现有功能可用。

## 1. 结论摘要

### 1.1 可行性判断

**有条件可行，但不能只靠 Restricted Token、Low Integrity 或 Job Object。**

能接近目标的稳定公开 API 组合是：

1. 每个 Task 一个独立 AppContainer SID；首版使用普通 AppContainer，LPAC 作为兼容性验证通过后的增强档。
2. 第一阶段直接用 AppContainer low-box token 作为目录 ACL 边界；Restricted Token 作为后续 defense-in-depth，避免影响现有 Win32/PowerShell 兼容。
3. 仅给 Task workspace、运行时文件和专用 IPC 对象授予该 AppContainer SID 的最小 ACL。
4. 用 Job Object 包住整个正常创建的子进程树，并设置 kill-on-close、进程数、内存、CPU 和生命周期限制。
5. 通过 `STARTUPINFOEX` 只继承 stdin/stdout/stderr 等明确列出的 HANDLE。
6. 构造全新的最小环境块，不继承 Bot 的环境变量。
7. 第一阶段为了继续使用 Redis，只做让 AppContainer 能连接现有 `localhost:{SchedulerPort}` 所必需的网络配置；这只是兼容措施，不是第一阶段的安全目标。目录隔离完成后，再单独处理 IPC 和网络限制。
8. Sandbox 内只运行最小 runner/shell，不加载 Bot 的 DI 容器、配置、数据库客户端或凭据。

这个组合可以让 `cmd.exe`、`powershell.exe`、`python.exe`、`node.exe`、`rundll32.exe` 等程序即使被任意调用，也仍使用相同的低权限 token。**可执行某个程序不等于获得该程序在宿主用户上下文中的权限。**

但仍有两个必须通过 PoC 才能决定是否上线的条件：

- **兼容性条件**：CMD 有微软 LPAC 示例；Windows PowerShell、PowerShell 7、Git Bash/MSYS2、Python、Node 只能说机制上可以启动普通 Win32 EXE，不能在未测试前承诺完整可用。它们可能依赖 registry、COM、ConPTY、named pipe、字体、证书库、模块目录、JIT 或未带 AppContainer ACL 的 DLL。
- **安全条件**：微软的 CMD LPAC 示例要求 `lpacCom` 和 `registryRead`。一旦开放 COM/RPC broker，必须验证 WMI、Task Scheduler、Shell COM、BITS 等是否可能代替调用者创建脱离 Job 或拥有更多权限的进程/副作用。Job 文档明确指出 `Win32_Process.Create` 创建的进程不自动进入调用方 Job。这不是可以忽略的兼容性细节。

因此，推荐判断是：

- **适合**：隔离可信 Windows 安装上的 LLM 任意命令，保护 Bot 文件、其他 Task、用户文件和本地服务，接受 Windows 内核/系统 broker 是 TCB。
- **不等价于 VM**：不适合把未知恶意二进制当作与宿主内核隔离的样本执行环境。
- **不能用 Restricted Token 单独实现**：普通 Low IL 默认阻止写高完整性对象，但仍可读取 DACL 允许的文件；Restricted Token 也不自带 AppContainer 的默认拒绝资源和网络模型。

### 1.2 新实验性 API

微软已公开：

- `Experimental_CreateProcessInSandbox`
- `Experimental_CreateProcessAsUserInSandbox`

它们位于 `processmodel.dll`，接受 FlatBuffer `SandboxSpec`，可以声明 AppContainer、`fs_read_only`、`fs_read_write`、network policy、capabilities、integrity、Win32k 和 Job UI 限制。本机存在 `C:\Windows\System32\processmodel.dll`，文件版本为 `10.0.26100.8737`。

但该接口明确标为 **Experimental**，目前需要 `LoadLibraryExW` + `GetProcAddress`，没有稳定 SDK 头文件。生产实现不应只依赖它；PoC 可以同时比较：

- A：稳定公开 AppContainer/Token/Job/ACL API 手工组合。
- B：实验性 Create Process In Sandbox API。

B 若可用，可显著减少 ACL 和策略拼装错误，但必须提供版本检测和 A 路径回退，且启动时必须 fail closed。

### 1.3 本机 PoC 结果

已在当前 Windows 11 `10.0.26200.8875` 上编译微软 `SandboxSecurityTools/LaunchAppContainer`，先使用微软示例中的 `lpacCom`、`registryRead` capabilities 做对照，再验证普通 AppContainer 在 **零 capability** 下运行。参考工具原项目固定 VS2022 `v143`，本机用已安装的 VS 18 `v145` toolset 成功构建。

实测结果：

| 测试 | 普通 AppContainer | LPAC | 结论 |
|---|---:|---:|---|
| `cmd.exe` 执行并写授权 workspace | 未单独区分 | 成功 | CMD 可作为首版 shell |
| `git.exe --version`，工作目录为授权 workspace | 未单独区分 | 成功，`2.52.0.windows.1` | 原生 Git 可用 |
| Windows PowerShell 5.1 执行 `.ps1` 并写 workspace | 成功 | 失败 | 首版不能默认 LPAC |
| Git Bash/MSYS2 `bash.exe --version` | 退出 `66` | 退出 `66` | 不能无损替代 Sandboxie 的 Git Bash 支持 |
| 读取未授权的项目 `README.md` | `Access denied`，读取 0 字节 | `Access denied`，读取 0 字节 | 普通 AppContainer 已提供有效文件边界 |
| 写入带 AppContainer ACL + Low IL 的 workspace | 成功 | 成功 | workspace 授权模型成立 |

LPAC 下 Windows PowerShell 5.1 的实际失败链为：

```text
System.Management.Automation.AmsiUtils
 -> PSEtwLog
 -> PSEtwLogProvider
 -> EventProvider.EtwRegister()
 -> Win32Exception: Access denied
```

普通 AppContainer 下 PowerShell 脚本成功写出 `PS_OK`，但产生 `FileSystem` provider 初始化默认 drive 失败的警告，说明核心命令可用，部分 provider/drive 仍需兼容测试。进一步移除全部 capability 后，CMD 仍写出 `CMD_NOCAPS_OK`，PowerShell 仍写出 `PS_OK` 并退出 `0`。因此生产默认 capability 可以为空，不需要为了启动 shell 预先授予 `lpacCom`、`registryRead` 或任何网络 capability。

Git Bash 的失败并非简单的 EXE/DLL 读取 ACL：`bash.exe`、`msys-2.0.dll` 已有 `ALL RESTRICTED APPLICATION PACKAGES (RX)`，且普通 AppContainer 与 LPAC 均退出 `66`。更可能是 MSYS2 对 named object、shared memory、console/PTY 或初始化环境的假设与 AppContainer 冲突。首版应支持 CMD、Windows PowerShell 和原生 `git.exe`，把 Git Bash 标为不支持；若 Bash 是硬需求，需单独研究 MSYS2 兼容或采用纯 Win32 shell，不能声称 drop-in 替换。

PoC 还确认了 `lpCurrentDirectory` 必须显式设置为授权 workspace。微软参考 launcher 传 `NULL` 并继承不可访问的宿主项目目录时，Git 报“当前目录无效”；切换到授权目录后立即正常。

因此迁移结论调整为：**有戏，第一阶段使用普通 AppContainer + Job Object，以 package SID + NTFS ACL 实现目录隔离。** Restricted Token 和 LPAC 留到后续硬化；本机 PowerShell 5.1 在 LPAC 下不可用。

## 2. 威胁模型

### 2.1 要防御

假设 Task 内代码完全不可信，可以：

- 执行任意 shell、EXE、DLL、脚本、native syscall。
- 枚举文件、registry、process、named objects、pipes、COM/RPC 服务和网络端点。
- 创建任意数量的正常子进程、尝试后台驻留和拒绝退出。
- 使用 junction、symlink、hardlink、UNC path、device path、alternate data stream 等路径形式。
- 读取自身 token、环境、命令行、内存和继承 HANDLE。
- 攻击同一 Task 内其他进程。

目标是它只能访问显式授权的 Task workspace、运行时依赖和窄 IPC；不同 Task 相互隔离。

### 2.2 不在保证内

这套方案明确不能防御：

- Windows kernel、win32k、驱动或允许访问的系统 broker 的提权/沙箱逃逸漏洞。
- 已被管理员或其他恶意软件控制的宿主机。
- Bot/broker 自己的内存安全、反序列化、路径验证或 confused-deputy 漏洞。
- 管理员、SYSTEM、调试权限持有者、物理攻击者。
- 已授权 workspace 内容被破坏或删除。
- 已允许网络后的数据外传和远端副作用。
- CPU cache、时间、存储占用等侧信道和不能被配额完全消除的 DoS。
- FAT/exFAT 等没有 NTFS DACL/MIC 语义的卷。
- NTFS journal、pagefile、crash dump、AV/索引、备份或 SSD 中的数据残留；“销毁 Task”不是取证级安全擦除。

## 3. 十二个重点问题的回答

### 3.1 普通 Win32 程序能否运行在 AppContainer / Restricted Token 中

**能启动，不代表能正常工作。**

- AppContainer 不要求目标 EXE 是 UWP/MSIX。非打包 launcher 可以用 `CreateAppContainerProfile`、`SECURITY_CAPABILITIES`、`PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` 和 `CreateProcess[AsUser]W` 启动普通 Win32 EXE。
- Restricted Token 可作为 primary token 传给 `CreateProcessAsUserW`。
- LPAC 比普通 AppContainer 更严格，不接受很多授予 `ALL APPLICATION PACKAGES` 的环境权限；目标 EXE、DLL、资源文件和 registry 依赖必须有明确可达权限。
- 微软 `SandboxSecurityTools/LaunchAppContainer` 给出了 `cmd.exe` 的 LPAC 运行示例，示例需要 `lpacCom` 与 `registryRead` capability。

### 3.2 PowerShell、CMD、Git Bash/Bash 和任意子进程

- `cmd.exe`：官方参考实现证明可以在 LPAC 中启动；仍需实测批处理、管道、重定向、ConPTY 和常用内建命令。
- Windows PowerShell 5.1：是普通 Win32/.NET Framework 程序，但高度依赖 registry、COM、模块、证书和系统服务，兼容风险最高。
- PowerShell 7：通常更适合作为目标，但依赖 CoreCLR/JIT、安装目录 DLL、模块和 native library。不能开启 `ProhibitDynamicCode`/ACG，否则 JIT 很可能失败。
- Git Bash/MSYS2：需要给 Git 安装目录 RX，验证 MSYS runtime、fork 模拟、PTY、named pipe、`git.exe` 和 helper 子进程。不能只验证 `bash --version`。
- Python/Node：可以继承相同 token；需给解释器/runtime RX 和 workspace RW。Node/.NET/Python 扩展可能需要动态代码或加载 workspace native DLL，因此 CIG/ACG/image-load mitigations 要按兼容性分级。

建议生产支持列表采用 allowlisted runtime 安装根目录，但**命令内容和由这些 runtime 执行的代码不做语义拦截**。allowlist 的目的只是确保所需二进制可加载，不是命令过滤。

### 3.3 子进程能否可靠继承限制

普通 `CreateProcess` 子进程通常继承父进程 AppContainer token；Job 中进程创建的正常子进程默认进入同一 Job。不要设置：

- `JOB_OBJECT_LIMIT_BREAKAWAY_OK`
- `JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK`

初始目标必须以 `CREATE_SUSPENDED` 启动，先 `AssignProcessToJobObject`，再 `ResumeThread`，否则存在它先执行/派生再入 Job 的竞争窗口。

但“整个进程树可靠”不能仅靠 Job：

- 文档明确说 WMI `Win32_Process.Create` 创建的进程不自动关联调用方 Job。
- COM/RPC/服务/计划任务本质上可能是宿主 broker 代做操作。
- 因此 LPAC 下应尽量不授予 `lpacCom`；若 shell 启动必须授予，WMI/COM 逃逸测试是上线阻断项。
- 即使第三方 broker 创建的进程不在 Job，也必须验证其 token 仍是 AppContainer/Restricted Token，不能仅检查父子 PID。

### 3.4 只访问指定 workspace

为每个 Task 创建唯一 AppContainer profile/SID，例如：

```text
TelegramSearchBot.Task.<random-128-bit-id>
```

在专用 NTFS workspace 上：

1. 禁止从宿主敏感父目录继承宽 ACL，使用受控根目录。
2. 保留 broker/维护账户所需权限。
3. 只给该 Task package SID 所需的 `RX` 或 `RWX`，使用对象/容器继承。
4. 给目录设置允许 Low IL 写入的 mandatory label；DACL 仍须命中该 Task SID，Low label 本身不会授予访问。
5. 只给 runtime 目录 package SID `RX`，绝不 `W`。
6. 不给 `ALL APPLICATION PACKAGES` 或 `ALL RESTRICTED APPLICATION PACKAGES` 广泛写权限。

AppContainer 访问是普通 user/group 权限与 package/capability 权限的交集。因此即使宿主用户能读整个磁盘，没有该 AppContainer SID/capability 的对象仍应拒绝访问。

Junction/symlink 不能凭空增加 token 权限：重解析到宿主路径后，目标对象仍会执行 ACL/AppContainer/MIC 检查。但 broker 在复制、回收、发布结果时必须防止 TOCTOU：按 handle 操作，检查 `FILE_ATTRIBUTE_REPARSE_POINT`、最终路径、volume/file ID，并避免让高权限 broker 跟随 Task 创建的链接写到外部。

### 3.5 任意解释器、rundll32、COM、pipe、registry 的绕过

- `cmd`、PowerShell、Python、Node、`rundll32` 只能在当前 token 下执行；换一个系统 EXE 不会恢复被删除的 SID/privilege。
- DLL 在 `rundll32` 进程内仍使用同一 token。高风险来自 DLL/系统服务漏洞，而不是文件名。
- Registry 是 securable object。LPAC 默认更严格；`registryRead` 会扩大可读面，必须测试是否暴露产品密钥、连接信息和第三方凭据。
- Named pipe 必须使用随机每 Task 名称和显式 DACL，不能接受默认 DACL。AppContainer 场景按要求使用 `LOCAL\...` 命名。
- COM/RPC 是最大 broker 面。允许的 COM server 可能在高权限进程中执行操作，安全性取决于 server 是否正确识别 AppContainer caller。
- 不允许 Sandbox 打开共享 Garnet/Redis、Bot 管理 pipe、Docker named pipe、SSH agent、浏览器调试端口等宿主 IPC。

Windows ACL/AppContainer 可以阻止直接资源访问，但不能修复一个把高权限操作暴露给低权限 caller 的宿主服务。

### 3.6 防止读取 Bot 配置、环境变量、凭据、数据库和其他 Task

必须同时处理“可命名资源”和“启动时带进去的资源”：

- Bot 的 `%LOCALAPPDATA%\TelegramSearchBot\Config.json`、`Data.sqlite`、日志、向量索引和其他 Task 根目录不授予 Task package SID。
- 不把完整 TelegramSearchBot 进程作为 sandbox ToolHost。创建独立最小 runner 项目，不引用 `Env`；当前 `Env` 静态初始化会直接读取 `Config.json`。
- `CreateProcessAsUserW(lpEnvironment = NULL)` 会继承调用方环境。必须构造 allowlist 环境块，只保留 `SystemRoot`、`ComSpec`、受控 `PATH`、locale 和 Task 自己的 `TEMP/TMP/HOME/USERPROFILE`。
- API key、Bot token、数据库连接、Scheduler port、代理凭据和 CI secrets 一律不进入环境、命令行或可继承 handle。
- 不共享 Bot 内存映射、日志 sink、credential handle、token handle 或数据库连接。
- 每 Task 使用不同 AppContainer SID、Job、workspace、temp、IPC 名称；同一 SID 的两个 Task 不能视为隔离。

当前 Sandboxie ToolHost 路径启动的是当前可执行文件、加载完整服务，并通过共享 Redis/Garnet 通信；这不满足上述“最小无凭据 runner”要求，不能直接平移到 AppContainer。

### 3.7 HANDLE inheritance

把 HANDLE 当作不可伪造的 capability。已打开 handle 可以绕过后续按名称打开时的 ACL 检查。

推荐：

1. 默认所有 broker handle 非 inheritable。
2. 用 `STARTUPINFOEX` + `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` 只列出三个匿名 pipe/必要 IPC handle。
3. pipe 的 broker 端立即用 `SetHandleInformation(..., HANDLE_FLAG_INHERIT, 0)` 清除继承。
4. 不继承 Job、process、thread、token、section、registry、file、socket、named pipe server 或 completion-port handle。
5. 启动后在 target 内枚举 handle 做 PoC 审计。

不能只依赖 `.NET ProcessStartInfo` 的默认值来证明安全；生产 launcher 应直接控制 Win32 process creation 参数。

### 3.8 进程树、CPU、内存、数量和生命周期

Job Object 非常适合这部分，但它不是文件/网络 sandbox。

每 Task 独立 Job，至少设置：

- `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`
- `JOB_OBJECT_LIMIT_ACTIVE_PROCESS`
- `JOB_OBJECT_LIMIT_JOB_MEMORY`，必要时再加 per-process memory
- `JOBOBJECT_CPU_RATE_CONTROL_INFORMATION` hard cap 或 weight
- per-job user time / wall-clock supervisor timeout
- UI restrictions（若兼容）
- I/O completion port 监听创建、退出、limit violation

结束 Task 时调用 `TerminateJobObject`，等待 active process 为 0，再关闭 Job handle。当前项目的 Job 仅在进程已经启动后分配，并只配置 kill-on-close 和 per-process memory；对于敌对代码需要改成 suspended launch、aggregate job memory、active-process 和 CPU 限额。

Job 没有可靠的每 Task 磁盘容量限制。可选方案是专用受配额 volume/VHDX、按账户 NTFS quota，或由 broker 监控并在超限时终止；监控不是严格的瞬时配额安全边界。

### 3.9 网络、localhost、LAN 和数据库

最终安全目标仍是：**默认不给任何 network capability。** AppContainer 默认阻断网络，且 loopback 默认单独隔离。

但为了降低从 Sandboxie 迁移的首阶段风险，可以暂时保持现有 Redis/Garnet IPC：

1. 给普通 AppContainer token 增加 `internetClient` 和 `privateNetworkClientServer` capability。只需要客户端网络时不授予 `internetClientServer`，避免无必要的公网入站权限。
2. 第一阶段延续现有模型，为每个 chat 创建一个长期 AppContainer profile/SID，并给该 SID 添加 loopback exemption，使其可以继续连接 `localhost:{SchedulerPort}`。
3. 保留现有 Redis queue/result/heartbeat 协议，不在第一阶段引入 pipe broker。
4. 每次命令或 Task 仍使用独立 Job 和 workspace；但同一 chat 共享 package SID，所以不能声称同一 chat 内不同 Task 之间有 SID/ACL 隔离。
5. 明确把该阶段定义为“文件与进程隔离迁移版”，不声称提供网络、localhost、LAN 或 Redis 控制面隔离。

loopback exemption 是机器级配置。实现时优先用 `NetworkIsolationGetAppContainerConfig` 读取已有 SID 列表，再合并目标 SID 后调用 `NetworkIsolationSetAppContainerConfig`；setter 会替换列表，不能覆盖其他应用已有 exemption。也可以在 PoC 中使用 `CheckNetIsolation.exe LoopbackExempt -a -p=<AppContainer SID>`，但生产代码需要幂等添加、删除和崩溃恢复，并处理通常需要提升权限的问题。

这个第一阶段的风险必须接受并记录：

- Sandbox 内任意程序都能连接宿主 Garnet/Redis；如果服务没有身份认证和 key 级授权，它可以枚举或修改其他 chat/Task 队列、伪造结果、删除 key，甚至执行服务支持的管理命令。
- Sandbox 可以扫描和攻击其他 localhost 服务、LAN 服务和数据库端口。
- Sandbox 可以把已授权 workspace 内容外传到公网。
- 网络服务或协议解析器会进入 TCB；文件 ACL 和 AppContainer 不能修复一个向低权限 caller 暴露高权限操作的服务。

因此第一阶段仍应至少保证 Garnet 只监听 loopback、不暴露到 LAN；不要把 Bot token、数据库密码或其他凭据通过 Redis payload、环境变量或命令行发送给 ToolHost。Redis 未授权访问和跨 chat key 操作必须加入测试，并作为第二阶段 IPC 替换的阻断项。当前开发会话不是 elevated，不能假设 Bot 能直接维护机器级 loopback exemption；安装器或一次性管理员 provisioning helper 必须负责注册 per-chat SID，或者 Bot 明确以具备该权限的受控账户运行。

第二阶段再取消 loopback exemption 和 broad network capabilities，改用 package-SID ACL 的 named/anonymous pipe 或继承 HANDLE IPC。若以后确需公网：

1. 只授予 `internetClient`，不授予 `privateNetworkClientServer` 或 server capability。
2. 不添加 loopback exemption。
3. 用 WFP 在 ALE connect/receive-accept v4/v6 层按 `FWPM_CONDITION_ALE_PACKAGE_ID` 绑定 Task package SID。
4. 显式拒绝 loopback、宿主地址、RFC1918、CGNAT、IPv4 link-local、IPv6 loopback/link-local/ULA、LAN、云 metadata 和数据库端口。
5. 只在确有需求时放行 DNS/代理；代理本身必须认证 Task 身份。

WFP policy 安装通常需要提升权限，应放在极小的系统 broker 中并使用 dynamic session/事务，Task 回收时删除规则。

### 3.10 长期运行基础设施与大量 Task

可以长期运行一个 broker/supervisor；不需要一个 Task 一个 VM。但安全隔离单元仍应是：

```text
Task = unique AppContainer SID + workspace + temp + Job + IPC + optional WFP filters
```

可以复用只读 runtime 安装和 broker 进程，不能让不互信 Task 共享 AppContainer SID、可写目录、Job 或 worker 进程。每 Task 至少需要一个进程树；这是原生进程开销，不是 VM 开销。

大量 profile 的创建/删除、ACL 和 WFP 更新需要队列化、幂等和崩溃恢复。启动时扫描 orphan profile/workspace/filter/job metadata；profile 名使用不可猜随机 ID，不使用 chat ID 作为唯一安全边界。

### 3.11 每 Task 独立与完整销毁

推荐销毁顺序：

1. 停止接收新命令并关闭 broker IPC。
2. `TerminateJobObject`，确认所有进程退出。
3. 关闭 target 相关 HANDLE。
4. 删除 WFP dynamic filters/loopback 配置（正常设计不应有 loopback exemption）。
5. 以“不跟随 reparse point”的方式删除 workspace/temp/profile data。
6. `DeleteAppContainerProfile`。
7. 删除持久化 Task metadata。

销毁是资源撤销和最佳努力清理，不是文件内容安全擦除，也不能撤销已发生的网络/外部系统副作用。

### 3.12 实际安全边界

边界由以下交集组成：

```text
有效权限 = base user/restricted token
         ∩ AppContainer/LPAC package + capabilities
         ∩ object DACL
         ∩ MIC mandatory policy
         ∩ network isolation/WFP
         + 已继承/复制的 HANDLE
         + 允许访问的 broker/COM/RPC 接口
```

Job Object 负责进程拓扑和资源，不参与文件 ACL 授权。Process mitigation 降低漏洞可利用面，也不负责 workspace 隔离。

## 4. 推荐架构

```text
TelegramSearchBot (medium IL, owns secrets)
        |
        | authenticated local IPC; no secret payloads
        v
Sandbox Broker / Supervisor (minimal Windows-only component)
        |
        | create task identity, ACL, job, clean env, pipes
        | CREATE_SUSPENDED -> AssignProcessToJobObject -> ResumeThread
        v
Task Runner (AppContainer low-box token, no Bot assemblies/config)
        |
        +-- cmd.exe / powershell.exe / bash.exe
        +-- python/node/git/rundll32/any normal child
        |
        `-- unique task workspace/temp only
```

### 4.1 Broker

职责只包括 policy 编译、launch、stdin/stdout/stderr、Job 监督、超时和清理。它不能提供“读取任意路径”“以 Bot 身份发 HTTP”“执行任意 COM”等万能代理。

首版可以在 Bot 进程内实现 Windows-only launcher，但安全成熟后应考虑独立 broker：减少 Bot 巨大依赖图成为 target 可攻击 IPC 面的概率。若引入 LocalSystem/WFP 服务，应再拆分为极小 provisioning service；不要让 LocalSystem broker 暴露通用文件或进程 API。

### 4.2 Task Runner

Runner 应是新建的小项目，不能引用 `TelegramSearchBot.Common.Env`。协议只需要：

- 接收 command、cwd、timeout。
- 启动 shell 并流式返回 stdout/stderr/exit code。
- 响应取消。
- 不持有 Bot token、Redis credential、数据库连接或 HTTP client credential。

如果每个命令都由 broker 直接启动 shell，可以不要 runner；若要长期会话、PTY 或多命令状态，才保留 runner。

### 4.3 身份选择

推荐分阶段部署：

1. **阶段 1：目录访问隔离**。普通 AppContainer + per-chat package SID + Job Object；只给指定 workspace/输入目录配置 ACL，保留 Redis、心跳、队列和网络。验收重点是未授权目录访问被 Windows 拒绝。
2. **阶段 2：IPC 与网络隔离**。改为 package-SID ACL 的本地 pipe/HANDLE IPC，取消 loopback exemption，默认不给网络 capability。
3. **阶段 3：更严格模式**。按需评估 per-Task SID、LPAC 和 WFP。LPAC 仅用于已验证兼容的 CMD/原生工具；本机 Windows PowerShell 5.1 在 LPAC ETW 初始化失败。
4. **独立低权限本地账户 + AppContainer**：需要进一步隔离 Bot 用户资源时采用，运维成本较高。
5. **Restricted Token + Low/Untrusted IL + ACL**：仅兼容性回退，不应声称与 AppContainer 等价。

不推荐依赖未文档化的 `NtCreateLowBoxToken`、Job silo/server silo API。Windows container silo 没有稳定公开的通用 user-mode 创建 API。

## 5. 各机制的职责

| 机制 | 负责 | 不负责/注意 |
|---|---|---|
| Restricted Token | 删除 privilege、禁用管理员/用户组、限制 DACL 授权 | 不自动阻止读取用户本来可读的资源；不是网络 sandbox |
| AppContainer | package identity、capability/default-deny、进程/凭据/网络隔离 | 兼容性依赖；允许的 broker 仍是攻击面 |
| LPAC | 移除普通 AppContainer 对 `ALL APPLICATION PACKAGES` 的 ambient access | runtime/registry/COM ACL 配置更繁琐 |
| NTFS ACL | 精确授权 workspace、runtime、pipe、registry | 只对 securable object 有效；broker 必须防 reparse/TOCTOU |
| MIC/Low IL | 主要阻止 write-up、降低 UI interaction | 默认通常不阻止 read-up，不能单独保护秘密 |
| Job Object | 子进程树、kill、CPU、内存、数量、UI restrictions | 不限制文件、registry、network；brokered/WMI process 可能不在 Job |
| HANDLE allowlist | 防止把 Bot 已打开资源直接交给 target | 一个泄漏 handle 就可能绕过按名称 ACL |
| WFP/AppContainer network | 限制公网、LAN、localhost、入站/出站 | WFP 管理需高权限；规则必须同时覆盖 IPv4/IPv6 |
| Process mitigations | 降低 win32k、DLL、JIT、extension-point 攻击面 | ACG/CIG/Win32k lockdown 会破坏大量 shell/runtime |
| Alternate desktop/window station | 防窗口消息、hooks、clipboard/UI 攻击 | headless + Win32k lockdown 时价值降低；不是文件边界 |
| Minimal broker | 提供经过验证的少量特权操作 | broker policy/解析错误会成为直接逃逸 |

## 6. C#/.NET 所需关键 API

### 6.1 Profile、SID、token

- `CreateAppContainerProfile`
- `DeriveAppContainerSidFromAppContainerName`
- `DeleteAppContainerProfile`
- `DeriveCapabilitySidsFromName`
- `OpenProcessToken`
- `CreateRestrictedToken`
- `DuplicateTokenEx`
- `SetTokenInformation(TokenIntegrityLevel, ...)`
- `GetTokenInformation(TokenIsAppContainer/TokenAppContainerSid/TokenCapabilities/TokenIsLessPrivilegedAppContainer)`
- `CreateEnvironmentBlock` / `DestroyEnvironmentBlock`，或完全手工构造 allowlist Unicode environment block

### 6.2 启动与 HANDLE

- `InitializeProcThreadAttributeList`
- `UpdateProcThreadAttribute`
- `DeleteProcThreadAttributeList`
- `CreateProcessAsUserW`
- `CreatePipe` / `CreateNamedPipeW`
- `SetHandleInformation`
- `ResumeThread`

需要的 attributes：

- `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`
- `PROC_THREAD_ATTRIBUTE_ALL_APPLICATION_PACKAGES_POLICY`（LPAC）
- `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`
- `PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY`

以及：

- `SECURITY_CAPABILITIES`
- `STARTUPINFOEXW`
- `EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT | CREATE_SUSPENDED`

### 6.3 Job

- `CreateJobObjectW`
- `SetInformationJobObject`
- `AssignProcessToJobObject`
- `QueryInformationJobObject`
- `TerminateJobObject`
- `CreateIoCompletionPort` / `GetQueuedCompletionStatus`

结构至少包括：

- `JOBOBJECT_EXTENDED_LIMIT_INFORMATION`
- `JOBOBJECT_CPU_RATE_CONTROL_INFORMATION`
- `JOBOBJECT_ASSOCIATE_COMPLETION_PORT`
- 可兼容时的 `JOBOBJECT_BASIC_UI_RESTRICTIONS`

### 6.4 ACL/MIC

- `GetNamedSecurityInfoW` / `SetNamedSecurityInfoW`
- `SetEntriesInAclW`
- `ConvertStringSecurityDescriptorToSecurityDescriptorW`
- `SetSecurityInfo(..., LABEL_SECURITY_INFORMATION, ...)`
- 文件 handle 查询：`GetFinalPathNameByHandleW`、`GetFileInformationByHandleEx`

.NET 可用 `FileSystemAclExtensions`/`DirectorySecurity` 封装常规 DACL，但 AppContainer SID、mandatory label、WFP 和 `STARTUPINFOEX` 仍需 P/Invoke。所有 native HANDLE/SID/ACL 内存都应有 `SafeHandle`/RAII 包装。

### 6.5 Network/WFP

- `FwpmEngineOpen0` / `FwpmEngineClose0`
- `FwpmTransactionBegin0` / commit/abort
- `FwpmProviderAdd0`
- `FwpmSubLayerAdd0`
- `FwpmFilterAdd0`
- ALE connect/receive-accept v4/v6 layers
- `FWPM_CONDITION_ALE_PACKAGE_ID`

### 6.6 实验性接口

- `LoadLibraryExW("processmodel.dll", ..., LOAD_LIBRARY_SEARCH_SYSTEM32)`
- `GetProcAddress("Experimental_CreateProcessInSandbox")`
- `GetProcAddress("Experimental_CreateProcessAsUserInSandbox")`
- FlatBuffers `SandboxSpec.fbs` / `SBOX` blob

建议第一版稳定 API 用单独 Windows-only C# library + CsWin32/手写 `LibraryImport`；若 marshalling 和 ACL/WFP 代码变得复杂，改为小型 C++ launcher/helper 比在 Bot 内堆大量不安全 P/Invoke 更容易审计。

## 7. 当前项目的具体影响

现有实现可作为兼容性基线，但不是本文目标边界：

- `SandboxieToolHostService` 是 Sandboxie-Plus driver/virtualization 方案，不是只依赖公开 AppContainer/ACL API。
- box 当前按 chat 而非 Task 隔离，且配置含 `NeverDelete=y`。
- `SandboxieDenyHostFileSystem` 默认值为 `false`；仅关闭若干敏感路径不能证明“其余宿主文件默认不可读”。
- ToolHost 启动当前 TelegramSearchBot 可执行文件，`SandboxToolConsumer` 使用完整 DI scope。
- ToolHost 通过 `127.0.0.1` Garnet/Redis 队列通信；这与“不开放 localhost”的目标冲突。
- `BashToolService` 默认 working directory 是 `Env.WorkDir`，新路径必须强制为 Task workspace。
- 现有 `AppBootstrap.ChildProcessManager` 已有 Job 封装，但进程先启动后入 Job，且只设置 kill-on-close 和 per-process memory，不足以启动敌对代码。

因此建议新建独立接口，例如：

```text
ISandboxBroker.CreateTaskAsync(policy)
ISandboxTask.ExecuteAsync(command, cwd, timeout)
ISandboxTask.TerminateAsync()
ISandboxTask.DisposeAsync()
```

不要在原 `Process.Start` 周围逐项打补丁后宣称完成安全隔离。

## 8. 最小 PoC 验证矩阵

PoC 不是只验证“命令返回 0”，而要同时验证允许路径、拒绝路径、token、进程树和 IPC/network。

### Phase A：稳定 API launcher 与现有 Redis IPC

1. 创建或复用 per-chat AppContainer profile，打印并断言 package SID。
2. 启动后查询 token，断言 IL 为 Low、`TokenIsAppContainer=1` 且 package SID 与 profile 一致；Restricted Token 放到后续硬化 PoC。
3. 为 Task NTFS workspace 配该 chat SID RW 和 Low mandatory label；记录同 chat 任务共享 SID 的过渡期限制。
4. 清空环境，只传 allowlist；第一阶段仅通过参数传入 chat ID、Redis endpoint、workspace 和父进程标识，不让 ToolHost 读取 Bot `Config.json`。
5. 用 handle list + suspended launch + 独立 Job 启动测试程序。
6. 第一阶段授予 `internetClient`、`privateNetworkClientServer`，由提升权限的 provisioning 路径幂等添加当前 per-chat AppContainer SID 的 loopback exemption，验证现有 Redis queue/result/heartbeat 全链路。
7. 在 target 内枚举 token、privileges、groups、capabilities、IL、Job membership、environment、handles。
8. 验证 AppContainer profile 删除时同步删除 loopback exemption；模拟崩溃后由启动扫描清理 orphan exemption。

### Phase B：shell/runtime 兼容

逐项测试：

- `cmd.exe /d /s /c`: echo、dir、重定向、管道、批处理、后台 child。
- Windows PowerShell 5.1 `-NoProfile -NonInteractive`：filesystem、pipeline、module import、native child、COM/WMI 失败预期。
- PowerShell 7（安装后）：同上。
- Git Bash：`bash -lc`、MSYS pipe、`git status`、`git diff`、child tree、PTY/无 PTY。
- Python、Node、git、编译器等实际 Task 依赖。

每个 runtime 记录为启动所增加的 ACL/capability；若必须增加 broad capability，重新做安全评估。

### Phase C：必须拒绝的资源

从每种 shell 和 native test EXE 尝试：

- 读/写 Bot `Config.json`、`Data.sqlite`、logs、进程可执行目录中的敏感测试文件。
- 读用户 `.ssh`、`.aws`、`.azure`、浏览器 profile、Credential Manager/DPAPI test secret。
- 读写另一 Task workspace/temp/profile。
- 枚举/打开 Bot process、token、memory、named objects。
- 访问 HKCU/HKLM 敏感测试 key。
- 连接 Bot/Garnet、本机数据库、Docker pipe、SSH agent pipe。
- 连接 `127.0.0.1`、`::1`、宿主 LAN IP、RFC1918、IPv6 ULA、公网。

预期拒绝必须由 Win32 error/token/WFP trace 证明，而不是仅靠 shell 文本。

### Phase D：绕过与 confused deputy

必须覆盖：

- `cmd -> powershell -> python/node -> child` 多层继承。
- `rundll32`、`mshta`、`regsvr32`、WMI `Win32_Process.Create`、Task Scheduler、Shell COM、BITS。
- named pipe 猜测/枚举、Redis/Garnet 未认证访问。
- junction、symlink、hardlink、UNC、device path、ADS、8.3 path、case/normalization。
- 继承/重复 process、token、file、socket、section、pipe handle。
- 尝试 `CREATE_BREAKAWAY_FROM_JOB`、nested job、orphan/daemon。
- target 在 broker 清理期间并发替换目录为 reparse point。

如果授予 `lpacCom` 后 WMI/COM 能产生非 AppContainer token 或越过 workspace 的副作用，LPAC shell 路线应判定为不满足目标，而不是增加命令黑名单。

### Phase E：配额与清理

- fork bomb 命中 active-process limit。
- 单进程/多进程内存命中 aggregate Job limit。
- CPU hard cap、wall timeout、取消和 Bot 崩溃后 kill-on-close。
- 大量 stdout/stderr 不撑爆 Bot 内存。
- 大文件/小文件耗尽磁盘场景。
- 强制结束后无存活 PID、无 WFP filter、无 profile、无可访问 workspace；重启 broker 可清理 orphan metadata。

### Phase F：实验性 API 对照

在本机对 `processmodel.dll` 做 `GetProcAddress`，用相同测试矩阵比较声明式 `fs_read_only/fs_read_write/network_policy` 与手工 API。任何 symbol/schema/OS build 不匹配都必须安全失败并回退稳定实现，不能无 sandbox 启动。

## 9. 上线门槛

只有同时满足以下条件才应称为“Windows 原生 Sandbox”：

- 所有目标 shell/runtime 通过正向兼容测试。
- 负向文件、registry、环境、HANDLE、IPC、localhost/LAN、跨 Task 测试全部由内核拒绝。
- 任意正常子进程保持 package SID/restricted token/Job。
- `lpacCom`/`registryRead` 增权经过单独攻击面测试。
- broker 不加载 Bot secrets，IPC 是每 Task 认证和 ACL 隔离的。
- 崩溃恢复与回收测试通过。
- Windows build/runtime/ACL 前置条件在启动时验证，失败时 fail closed。

若 PowerShell/Git Bash 为正常运行必须开放能产生高权限 broker 副作用的 COM/RPC/capability，则应直接判定该 shell 在此安全级别下不受支持，或改用 Sandboxie/VM 级方案；不能用命令过滤掩盖边界缺口。

## 10. 参考资料

Microsoft 官方：

- [Create Process In Sandbox APIs](https://learn.microsoft.com/en-us/windows/win32/secauthz/createprocessinsandbox)
- [Launch an AppContainer](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)
- [AppContainer isolation](https://learn.microsoft.com/en-us/windows/win32/secauthz/appcontainer-isolation)
- [Restricted Tokens](https://learn.microsoft.com/en-us/windows/win32/secauthz/restricted-tokens)
- [Mandatory Integrity Control](https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control)
- [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
- [UpdateProcThreadAttribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)
- [Create processes / handle inheritance](https://learn.microsoft.com/en-us/windows/win32/procthread/creating-processes)
- [Named Pipe Security and Access Rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)
- [Windows application IPC](https://learn.microsoft.com/en-us/windows/apps/develop/communication/interprocess-communication)

参考实现：

- [Microsoft SandboxSecurityTools / LaunchAppContainer](https://github.com/microsoft/SandboxSecurityTools/tree/main/LaunchAppContainer)
- [Chromium Windows sandbox design](https://chromium.googlesource.com/chromium/src/+/HEAD/docs/design/sandbox.md)
- [Sandboxie-Plus](https://github.com/sandboxie-plus/Sandboxie)

证据等级说明：Microsoft Learn 的安全模型/API 语义作为主要依据；Chromium 用于组合架构与工程经验；SandboxSecurityTools 用于证明普通 Win32/CMD 的启动方式；PowerShell/Git Bash 的完整兼容与所有 broker 绕过结论必须由目标 Windows build 上的 PoC 得出。

# Windows 平台依赖分析文档

## 概述

本文档详细分析 TelegramSearchBot 项目中存在的 Windows 平台特定依赖，这些依赖限制了项目在 Linux 和 macOS 等其他平台上的可移植性。

## Windows 特定依赖项

### 1. 运行时标识符限制

#### 1.1 TelegramSearchBot 主项目
- **文件**: `TelegramSearchBot/TelegramSearchBot.csproj`
- **问题**: 仅指定了 Windows 运行时标识符
  ```xml
  <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
  ```
- **影响**: 无法发布适用于 Linux 或 macOS 的版本

#### 1.2 TelegramSearchBot.Agent.PaddleOCR 项目
- **文件**: `TelegramSearchBot.Agent.PaddleOCR/TelegramSearchBot.Agent.PaddleOCR.csproj`
- **问题**: 强制指定 Windows 运行时
  ```xml
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  ```

### 2. Windows 特定 NuGet 包

#### 2.1 OpenCV 相关依赖
- **包名**: `OpenCvSharp4.runtime.win` (v4.11.0.20250507)
- **包名**: `OpenCvSharp4.Windows` (v4.11.0.20250507)
- **文件**: `TelegramSearchBot/TelegramSearchBot.csproj`
- **问题**: 这些包仅包含 Windows 平台的原生库

#### 2.2 PaddleOCR 运行时
- **包名**: `Sdcb.PaddleInference.runtime.win64.mkl` (v2.6.1)
- **文件**: `TelegramSearchBot/TelegramSearchBot.csproj`
- **问题**: Windows 专用的 PaddleOCR 推理引擎运行时

#### 2.3 Agent 项目中的依赖
- **包名**: `OpenCvSharp4.runtime.win` (v4.8.0.20230708)
- **包名**: `OpenCvSharp4.Windows` (v4.8.0.20230708)
- **包名**: `Sdcb.PaddleInference.runtime.win64.mkl` (v2.5.1)
- **文件**: `TelegramSearchBot.Agent.PaddleOCR/TelegramSearchBot.Agent.PaddleOCR.csproj`

### 3. Windows API 调用

#### 3.1 进程管理 API
- **文件**: `TelegramSearchBot/AppBootstrap/AppBootstrap.cs`
- **问题**: 使用 Windows 特有的 Job Objects API
  ```csharp
  [DllImport("kernel32", CharSet = CharSet.Unicode)]
  private static extern IntPtr CreateJobObject(IntPtr a, string? lpName);
  
  [DllImport("kernel32")]
  private static extern bool SetInformationJobObject(...);
  
  [DllImport("kernel32", SetLastError = true)]
  private static extern bool AssignProcessToJobObject(...);
  
  [DllImport("kernel32", SetLastError = true)]
  private static extern bool CloseHandle(IntPtr hObject);
  ```
- **影响**: 子进程管理功能在 Linux/macOS 上无法工作

### 4. 平台检测逻辑

#### 4.1 条件平台处理
- **文件**: `TelegramSearchBot/Service/Tools/DenoJsExecutorService.cs`
- **问题**: 虽然实现了平台检测，但仍依赖 Windows 特定文件
  ```csharp
  _denoPath = Path.Combine(_denoDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "deno.exe" : "deno");
  ```

## 跨平台解决方案

### 1. 运行时标识符修改
```xml
<!-- 修改为支持多平台 -->
<RuntimeIdentifiers>win-x64;linux-x64;osx-x64</RuntimeIdentifiers>
```

### 2. OpenCV 跨平台替代
- **当前**: `OpenCvSharp4.runtime.win` + `OpenCvSharp4.Windows`
- **建议**: 
  - `OpenCvSharp4.runtime.ubuntu.20.04-x64` (Linux)
  - `OpenCvSharp4.runtime.osx-x64` (macOS)
  - 或使用 `OpenCvSharp4.runtime` 的通用版本

### 3. PaddleOCR 跨平台运行时
- **当前**: `Sdcb.PaddleInference.runtime.win64.mkl`
- **建议**:
  - Linux: `Sdcb.PaddleInference.runtime.linux-x64`
  - macOS: 考虑使用 ONNX Runtime 替代方案

### 4. 进程管理抽象
- **当前**: Windows Job Objects API
- **建议**: 实现跨平台进程管理抽象
  - Linux: 使用 `prctl` 和 `setsid`
  - macOS: 使用 `posix_spawn` 和进程组
  - 或使用 .NET 的 `Process` 类配合跨平台信号处理

### 5. 条件编译指令
添加平台特定的条件编译：
```csharp
#if WINDOWS
    // Windows-specific code
#elif LINUX
    // Linux-specific code
#elif OSX
    // macOS-specific code
#endif
```

## Docker 支持

### Dockerfile 示例
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["TelegramSearchBot/TelegramSearchBot.csproj", "TelegramSearchBot/"]
RUN dotnet restore "TelegramSearchBot/TelegramSearchBot.csproj"
COPY . .
WORKDIR "/src/TelegramSearchBot"
RUN dotnet build "TelegramSearchBot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TelegramSearchBot.csproj" -c Release -o /app/publish -r linux-x64 --self-contained false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelegramSearchBot.dll"]
```

## 迁移优先级

1. **高优先级**: 运行时标识符和 NuGet 包依赖
2. **中优先级**: OpenCV 和 PaddleOCR 运行时迁移
3. **低优先级**: Windows API 调用抽象化

## 测试策略

1. **单元测试**: 确保跨平台兼容性
2. **集成测试**: 验证 Docker 容器运行
3. **CI/CD**: 设置多平台构建管道
# Linux 部署指南

## 概述

TelegramSearchBot 现在支持 Linux 平台部署。本指南说明了在 Linux 系统上部署和运行 TelegramSearchBot 的要求。

## 系统要求

### 操作系统
- Ubuntu 20.04+ 或 Debian 11+
- 其他 Linux 发行版（可能需要调整依赖包名称）

### .NET 运行时
- .NET 9.0 运行时或 SDK

### 系统依赖包

```bash
# 更新包管理器
sudo apt update

# 安装基础依赖
sudo apt install -y libgomp1 libdnnl2 intel-mkl-full libomp-dev
```

## 项目配置

### 条件编译支持

项目已配置条件编译，根据目标平台自动选择合适的运行时包：

```xml
<!-- Windows 运行时包 -->
<PackageReference Include="OpenCvSharp4.runtime.win" Version="4.11.0.20250507" 
    Condition="'$(RuntimeIdentifier)' == 'win-x64' OR '$(RuntimeIdentifier)' == ''" />
<PackageReference Include="Sdcb.PaddleInference.runtime.win64.mkl" Version="3.1.0.54" 
    Condition="'$(RuntimeIdentifier)' == 'win-x64' OR '$(RuntimeIdentifier)' == ''" />

<!-- Linux 运行时包 -->
<PackageReference Include="OpenCvSharp4.runtime.linux-x64" Version="4.10.0.20240717" 
    Condition="'$(RuntimeIdentifier)' == 'linux-x64'" />
<PackageReference Include="Sdcb.PaddleInference.runtime.linux-x64.mkl" Version="3.1.0.54" 
    Condition="'$(RuntimeIdentifier)' == 'linux-x64'" />
```

## 编译和发布

### 编译项目

```bash
# 恢复依赖
dotnet restore TelegramSearchBot.sln

# 编译解决方案
dotnet build TelegramSearchBot.sln --configuration Release

# 运行测试
dotnet test
```

### 发布 Linux 版本

```bash
# 发布 Linux 独立版本
dotnet publish TelegramSearchBot/TelegramSearchBot.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --output ./publish/linux-x64
```

## 运行应用程序

### 使用提供的运行脚本

```bash
# 使用提供的 Linux 运行脚本
./run_linux.sh
```

### 手动设置环境变量

```bash
# 设置库路径
export LD_LIBRARY_PATH=/path/to/TelegramSearchBot/.nuget/packages/sdcb.paddleinference.runtime.linux-x64.mkl/3.1.0.54/runtimes/linux-x64/native:$LD_LIBRARY_PATH

# 运行应用程序
cd TelegramSearchBot
dotnet run
```

### 作为系统服务运行

创建 systemd 服务文件 `/etc/systemd/system/telegramsearchbot.service`：

```ini
[Unit]
Description=TelegramSearchBot
After=network.target

[Service]
Type=simple
User=telegrambot
WorkingDirectory=/opt/TelegramSearchBot
ExecStart=/opt/TelegramSearchBot/run_linux.sh
Restart=always
RestartSec=10
Environment=LD_LIBRARY_PATH=/opt/TelegramSearchBot/.nuget/packages/sdcb.paddleinference.runtime.linux-x64.mkl/3.1.0.54/runtimes/linux-x64/native

[Install]
WantedBy=multi-user.target
```

启用和启动服务：

```bash
sudo systemctl daemon-reload
sudo systemctl enable telegramsearchbot
sudo systemctl start telegramsearchbot
```

## 故障排除

### 常见问题

1. **库加载失败**
   ```
   Unable to load shared library 'paddle_inference_c'
   ```
   
   解决方案：
   - 确保已安装所有系统依赖包
   - 检查 LD_LIBRARY_PATH 环境变量设置
   - 验证 PaddleInference Linux 运行时包是否已安装

2. **权限问题**
   ```
   Permission denied
   ```
   
   解决方案：
   - 确保运行脚本有执行权限
   - 检查文件和目录权限

3. **模型文件缺失**
   ```
   Model file not found
   ```
   
   解决方案：
   - 确保模型文件已复制到输出目录
   - 检查配置文件中的模型路径

### 日志和调试

启用详细日志：

```bash
# 设置日志级别
export Logging__LogLevel__Default=Debug

# 运行应用程序
./run_linux.sh
```

## 性能优化

### CPU 优化
- 使用 MKL 数学库（已默认配置）
- 考虑使用 CPU 亲和性设置

### 内存优化
- 调整 GC 压力设置
- 配置适当的缓存大小

### 存储优化
- 使用 SSD 存储
- 配置适当的数据库连接池

## 安全考虑

### 文件权限
- 确保配置文件权限适当
- 限制对敏感数据的访问

### 网络安全
- 使用防火墙规则
- 配置适当的 TLS 设置

### 更新和维护
- 定期更新依赖包
- 监控安全公告

## 支持的平台

- ✅ Ubuntu 20.04 LTS
- ✅ Ubuntu 22.04 LTS
- ✅ Debian 11 (Bullseye)
- ✅ Debian 12 (Bookworm)
- 🔄 其他 Linux 发行版（可能需要调整）

## 联系支持

如果遇到问题，请检查：
1. 本指南的故障排除部分
2. 项目 GitHub Issues
3. 相关依赖库的文档
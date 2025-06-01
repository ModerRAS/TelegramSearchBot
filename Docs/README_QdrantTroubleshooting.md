# Qdrant向量数据库连接故障排除指南

## 问题现象

当看到类似以下错误时，说明Qdrant连接出现问题：

```
[ERR] 对话段 406 向量化失败
Grpc.Core.RpcException: Status(StatusCode="Unavailable", Detail="Error starting gRPC call. HttpRequestException: 由于目标计算机积极拒绝，无法连接。 (localhost:6334)")
```

## 🔍 快速诊断

### 1. 使用管理命令检查状态

在机器人中发送以下命令（需要管理员权限）：

```
/向量诊断
```

这会显示详细的诊断信息，包括：
- 连接状态
- 端口开放情况  
- Qdrant进程状态
- 配置文件状态
- 修复建议

### 2. 检查日志

查看应用启动日志中的相关信息：
```
[INFO] 开始初始化向量数据库...
[INFO] Qdrant服务连接正常，当前集合数量: 10
```

## 🛠️ 常见问题及解决方案

### 问题1: Qdrant服务未启动

**症状**：
- gRPC端口6334关闭
- 没有qdrant进程运行
- 连接被拒绝错误

**解决方案**：

1. **检查QdrantProcessManager状态**
   ```bash
   # Windows任务管理器查看是否有qdrant.exe进程
   # Linux/Mac: ps aux | grep qdrant
   ```

2. **重启应用**
   - QdrantProcessManager会在应用启动时自动启动Qdrant
   - 确保应用有足够权限下载和运行Qdrant

3. **手动启动Qdrant**（如果自动启动失败）
   ```bash
   # 进入工作目录
   cd %LOCALAPPDATA%/TelegramSearchBot
   
   # 启动Qdrant
   ./bin/qdrant.exe --config-path qdrant_data/production.yaml
   ```

### 问题2: 端口冲突

**症状**：
- 端口6334或6333被占用
- Qdrant启动失败

**解决方案**：

1. **检查端口占用**
   ```bash
   # Windows
   netstat -ano | findstr :6334
   netstat -ano | findstr :6333
   
   # Linux/Mac
   lsof -i :6334
   lsof -i :6333
   ```

2. **更改端口配置**
   
   在 `Config.json` 中修改：
   ```json
   {
     "QdrantHttpPort": 6335,
     "QdrantGrpcPort": 6336
   }
   ```

3. **终止冲突进程**
   ```bash
   # Windows (PID从netstat命令获取)
   taskkill /PID <PID> /F
   
   # Linux/Mac
   kill -9 <PID>
   ```

### 问题3: 配置文件问题

**症状**：
- 配置文件不存在
- Qdrant启动参数错误

**解决方案**：

1. **检查配置文件**
   ```
   工作目录: %LOCALAPPDATA%/TelegramSearchBot/qdrant_data/production.yaml
   ```

2. **重新生成配置**
   - 删除 `qdrant_data` 目录
   - 重启应用，会自动重新生成

### 问题4: 权限问题

**症状**：
- 无法下载Qdrant二进制
- 无法创建配置文件

**解决方案**：

1. **检查文件权限**
   ```bash
   # 确保工作目录可写
   %LOCALAPPDATA%/TelegramSearchBot/
   ```

2. **以管理员身份运行**
   - 右键应用程序 → "以管理员身份运行"

3. **手动下载Qdrant**
   ```bash
   # 下载地址 (Windows)
   https://github.com/qdrant/qdrant/releases/download/v1.14.0/qdrant-x86_64-pc-windows-msvc.zip
   
   # 解压到
   %LOCALAPPDATA%/TelegramSearchBot/bin/
   ```

### 问题5: 防火墙阻止

**症状**：
- 端口开放但仍然连接失败
- Windows安全中心警告

**解决方案**：

1. **添加防火墙例外**
   - Windows安全中心 → 防火墙和网络保护 → 允许应用通过防火墙
   - 添加 `qdrant.exe` 和应用程序

2. **临时禁用防火墙测试**
   - 如果禁用后正常，说明是防火墙问题

## 🔧 故障排除流程

### 步骤1: 基础检查
```bash
# 1. 检查进程
tasklist | findstr qdrant

# 2. 检查端口
netstat -ano | findstr :6334

# 3. 检查文件
dir %LOCALAPPDATA%\TelegramSearchBot\bin\qdrant.exe
dir %LOCALAPPDATA%\TelegramSearchBot\qdrant_data\production.yaml
```

### 步骤2: 使用管理命令
```
/向量诊断     # 获取详细诊断信息
/向量健康检查 # 快速检查连接状态
/向量初始化   # 重新初始化（如果Qdrant已修复）
```

### 步骤3: 重启服务
```bash
# 方案1: 重启应用
# 应用会自动启动QdrantProcessManager

# 方案2: 手动重启Qdrant
taskkill /IM qdrant.exe /F
cd %LOCALAPPDATA%\TelegramSearchBot
.\bin\qdrant.exe --config-path qdrant_data\production.yaml
```

### 步骤4: 验证修复
```
/向量状态     # 查看集合状态
向量搜索 测试  # 测试搜索功能
```

## 📊 监控建议

### 1. 定期健康检查
在cron或定时任务中运行：
```bash
# 每小时检查一次Qdrant状态
curl -f http://localhost:6333/collections || echo "Qdrant异常"
```

### 2. 日志监控
关注以下日志模式：
- `Qdrant服务连接失败`
- `向量化失败`
- `connection refused`

### 3. 性能监控
- Qdrant进程内存使用
- 响应时间
- 错误率

## 🔄 自动恢复

新增的 `VectorDatabaseConnectionService` 提供了：

1. **自动重试机制**
   - 连接失败时自动重试3次
   - 指数退避延迟

2. **智能错误检测**
   - 识别连接错误类型
   - 区分临时和永久性错误

3. **诊断和修复建议**
   - 自动网络诊断
   - 提供具体修复步骤

## 📞 技术支持

如果问题仍然存在，请提供以下信息：

1. `/向量诊断` 的完整输出
2. 应用启动日志
3. 系统信息（操作系统、.NET版本）
4. 网络环境（防火墙、代理设置）

这将帮助快速定位和解决问题。 
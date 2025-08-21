# Scripts 目录

这个目录包含了TelegramSearchBot项目的各种测试和构建脚本。

## 编译错误修复脚本

### `fix-test-compilation-errors.ps1`
**PowerShell脚本** - 用于修复TelegramSearchBot.Test项目中的常见编译错误（Windows环境）

### `fix-test-compilation-errors-quick.ps1`
**PowerShell快速版本** - 简化版本的编译错误修复脚本（Windows环境）

### `fix-test-compilation-errors.sh`
**Bash脚本** - 用于修复TelegramSearchBot.Test项目中的常见编译错误（Linux/macOS环境）

### `README-fix-test-compilation-errors.md`
详细的修复脚本使用说明和故障排除指南

## 测试脚本

### `run_tdd_tests.sh`
运行TDD（测试驱动开发）测试套件，验证核心功能的测试覆盖。

### `run_message_tests.sh`
运行Message领域相关的所有测试，包括单元测试和集成测试。

### `run_integration_tests.sh`
运行集成测试，验证各组件之间的交互。

### `run_controller_tests.sh`
运行控制器层的测试，验证API端点。

### `run_search_tests.sh`
运行搜索功能的测试，包括Lucene和向量搜索。

### `run_performance_tests.sh`
运行性能测试，验证系统的响应时间和吞吐量。

### `test_sendmessage_simple.sh`
简单的消息发送测试脚本。

## 使用方法

### 编译错误修复脚本

#### Windows (PowerShell)
```powershell
# 预览模式（推荐先运行）
.\scripts\fix-test-compilation-errors.ps1 -WhatIf

# 执行修复
.\scripts\fix-test-compilation-errors.ps1

# 使用快速版本
.\scripts\fix-test-compilation-errors-quick.ps1
```

#### Linux/macOS (Bash)
```bash
# 预览模式（推荐先运行）
./scripts/fix-test-compilation-errors.sh -w

# 执行修复
./scripts/fix-test-compilation-errors.sh

# 显示帮助
./scripts/fix-test-compilation-errors.sh -h
```

### 测试脚本

```bash
# 给脚本添加执行权限
chmod +x scripts/*.sh

# 运行特定测试
./scripts/run_tdd_tests.sh

# 或者使用bash运行
bash scripts/run_integration_tests.sh
```

## 修复的编译错误类型

### 1. 构造函数参数问题
- AltPhotoControllerTests.cs - 缺少messageExtensionService参数
- 其他Controller测试 - 构造函数参数不匹配

### 2. API变更问题
- MessageSearchQueriesTests.cs - 查询构造函数参数顺序错误
- MessageSearchRepositoryTests.cs - LuceneManager API参数顺序变更

### 3. 类型名称变更
- SearchResult类型 - 替换为Message类型
- SearchDocument类型 - 替换为Message类型

### 4. 缺失方法
- TestDataSet.Initialize() - 添加数据库初始化方法

### 5. 命名空间引用
- User和Chat类型 - 添加Telegram.Bot.Types引用
- 配置类型 - 修复Dictionary<string, string>到Dictionary<string, string?>的转换

## 注意事项

- 所有脚本都从项目根目录运行
- 确保已安装所有必要的依赖
- 某些测试可能需要特定的环境配置
- 修复脚本会自动创建备份文件
- 建议在运行修复脚本前先进行版本控制备份
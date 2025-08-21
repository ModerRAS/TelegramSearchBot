# TelegramSearchBot.Test 编译错误修复脚本使用说明

## 概述

本脚本用于自动修复TelegramSearchBot.Test项目中的常见编译错误，包括：

1. **构造函数参数问题** - 修复AltPhotoController等类的构造函数调用参数缺失
2. **API变更问题** - 修复因API变更导致的参数顺序错误
3. **类型名称变更** - 修复SearchResult等类型名称变更问题
4. **缺失方法** - 添加TestDataSet.Initialize等缺失方法
5. **命名空间引用** - 添加缺失的using语句

## 脚本文件

### 1. 完整版本：`fix-test-compilation-errors.ps1`
- 功能全面，包含详细的日志记录和错误处理
- 支持WhatIf模式预览
- 包含备份机制
- 自动验证修复结果

### 2. 快速版本：`fix-test-compilation-errors-quick.ps1`
- 简化版本，专注于核心修复
- 执行速度更快
- 适合快速修复

## 使用方法

### 基本用法

```powershell
# 在项目根目录执行
.\scripts\fix-test-compilation-errors.ps1

# 或使用快速版本
.\scripts\fix-test-compilation-errors-quick.ps1
```

### 高级用法

```powershell
# 指定项目路径
.\scripts\fix-test-compilation-errors.ps1 -ProjectPath "C:\path\to\project"

# 预览模式（不实际修改文件）
.\scripts\fix-test-compilation-errors.ps1 -WhatIf

# 详细输出
.\scripts\fix-test-compilation-errors.ps1 -Verbose
```

## 修复的具体问题

### 1. AltPhotoControllerTests.cs
- **问题**：构造函数调用缺少`messageExtensionService`参数
- **修复**：添加缺失的参数到构造函数调用

### 2. MessageSearchQueriesTests.cs
- **问题**：MessageSearchByUserQuery和MessageSearchByDateRangeQuery构造函数参数顺序错误
- **修复**：调整参数顺序，添加缺失的query参数

### 3. MessageSearchRepositoryTests.cs
- **问题**：SearchResult类型不存在，参数顺序错误
- **修复**：替换为Message类型，调整LuceneManager.Search调用参数

### 4. TestDatabaseHelper.cs
- **问题**：TestDataSet类缺少Initialize方法
- **修复**：添加Initialize方法实现

### 5. QuickPerformanceBenchmarks.cs
- **问题**：User和Chat类型未找到
- **修复**：添加using语句，使用完全限定名

### 6. MessageProcessingBenchmarks.cs
- **问题**：CreateLongMessage方法调用参数错误
- **修复**：移除多余的userId参数

### 7. IntegrationTestBase.cs
- **问题**：Dictionary<string, string>到Dictionary<string, string?>类型不匹配
- **修复**：更新为可空字符串类型

## 备份机制

脚本在修改文件前会自动创建备份文件，格式为：
```
原文件名.backup_YYYYMMDD_HHmmss
```

如果修复失败，脚本会自动恢复备份。

## 验证修复结果

脚本执行完成后会自动验证修复结果：
1. 尝试编译项目
2. 报告编译结果
3. 如果仍有错误，显示剩余的错误信息

## 注意事项

1. **执行前备份**：建议在执行前手动备份整个项目
2. **版本控制**：确保项目在Git版本控制下，便于回滚
3. **测试验证**：修复完成后建议运行测试验证功能
4. **逐步修复**：如果某个文件修复失败，可以手动修复后继续

## 手动修复指南

如果自动脚本无法解决问题，可以参考以下手动修复步骤：

### 修复构造函数参数
```csharp
// 错误的调用
new AltPhotoController(botClient, llmService, sendMessage, messageService, logger, messageExtensionService)

// 正确的调用
new AltPhotoController(botClient, llmService, sendMessage, messageService, logger, sendMessageService, messageExtensionService)
```

### 修复查询构造函数
```csharp
// 错误的调用
new MessageSearchByUserQuery(groupId, userId, limit)

// 正确的调用
new MessageSearchByUserQuery(groupId, userId, "", limit)
```

### 修复类型引用
```csharp
// 添加using语句
using Telegram.Bot.Types;

// 使用完全限定名
var user = new Telegram.Bot.Types.User();
var chat = new Telegram.Bot.Types.Chat();
```

## 常见问题

### Q: 脚本执行失败怎么办？
A: 检查文件路径是否正确，确保有文件写入权限，查看错误日志。

### Q: 修复后仍有编译错误？
A: 某些错误可能需要手动修复，请查看编译错误信息并参考手动修复指南。

### Q: 如何撤销修复？
A: 删除备份文件或使用Git回滚到修复前的提交。

## 技术支持

如果遇到问题，请检查：
1. PowerShell执行策略：`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`
2. .NET SDK版本：确保安装了.NET 9.0 SDK
3. 文件权限：确保对项目文件有读写权限

---

**注意**：此脚本专门为TelegramSearchBot项目设计，请勿在其他项目中使用。
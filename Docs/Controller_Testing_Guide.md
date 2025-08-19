# Controller层测试指南

## 概述

Controller层测试确保Telegram机器人的各种消息处理控制器能够正确工作。这些测试覆盖了消息存储、搜索、AI处理、B站链接处理等核心功能。

## 测试结构

```
TelegramSearchBot.Test/Controllers/
├── ControllerTestBase.cs           # 测试基类
├── Storage/
│   └── MessageControllerTests.cs    # 消息存储控制器测试
├── Search/
│   └── SearchControllerTests.cs    # 搜索控制器测试
├── AI/
│   └── AutoOCRControllerTests.cs   # OCR控制器测试
├── Bilibili/
│   └── BiliMessageControllerTests.cs # B站链接处理测试
└── Integration/
    └── ControllerIntegrationTests.cs # 控制器集成测试
```

## 测试覆盖范围

### 1. 基础结构测试 (ControllerBasicTests.cs)
- 验证所有Controller实现IOnUpdate接口
- 检查公共构造函数存在性
- 验证ExecuteAsync方法签名
- 检查Dependencies属性
- 验证命名空间一致性

### 2. MessageController测试
- 文本消息处理
- 图片消息（使用标题）
- 回复消息处理
- CallbackQuery处理
- 错误处理
- MediatR通知发布

### 3. SearchController测试
- 关键词搜索命令（"搜索 "）
- 向量搜索命令（"向量搜索 "）
- 语法搜索命令（"语法搜索 "）
- 群组/私聊标识
- 搜索选项构建
- 错误处理

### 4. AutoOCRController测试
- 图片OCR处理
- 文件下载
- LLM服务调用
- 大图片处理
- 标题信息传递
- 错误处理

### 5. BiliMessageController测试
- B站视频链接识别
- 多种链接格式支持
- 多链接处理
- 回复中的链接处理
- 非B站链接过滤

### 6. 集成测试
- 多Controller协作
- 依赖注入容器
- 高并发处理
- 错误恢复
- 不同消息类型处理

## 运行测试

### 运行所有Controller测试
```bash
./run_controller_tests.sh
```

### 运行特定Controller测试
```bash
# 只运行MessageController测试
dotnet test --filter "FullyQualifiedName~MessageControllerTests"

# 只运行搜索相关测试
dotnet test --filter "FullyQualifiedName~SearchControllerTests"

# 运行AI相关测试
dotnet test --filter "FullyQualifiedName~AutoOCRControllerTests"
```

### 运行基础结构测试
```bash
dotnet test --filter "FullyQualifiedName~ControllerBasicTests"
```

## 测试最佳实践

### 1. 测试数据准备
- 使用ControllerTestBase提供的辅助方法创建测试数据
- 使用Mock对象隔离外部依赖
- 避免硬编码测试数据

### 2. 验证模式
- 验证Controller行为而不是实现细节
- 检查服务调用次数和参数
- 验证PipelineContext的状态变化

### 3. 错误处理测试
- 测试服务不可用的情况
- 验证错误日志记录
- 确保系统优雅降级

### 4. 集成测试
- 测试多个Controller协作
- 验证依赖注入配置
- 模拟高并发场景

## Mock对象设置

### MessageService Mock
```csharp
_messageServiceMock
    .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
    .ReturnsAsync(1);
```

### BotClient Mock
```csharp
_botClientMock
    .Setup(x => x.GetFileAsync(It.IsAny<string>(), default))
    .ReturnsAsync(testFile);
```

### LLM Service Mock
```csharp
_llmServiceMock
    .Setup(x => x.GetOCRAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
    .ReturnsAsync("Extracted text");
```

## 性能考虑

Controller测试主要关注：
1. **正确性**：确保Controller按预期工作
2. **错误处理**：优雅处理各种异常情况
3. **集成能力**：与其他组件协同工作
4. **并发安全**：支持多消息并发处理

## 持续改进

Controller测试应该随着新功能的添加而扩展：
- 为新Controller添加测试
- 更新现有测试覆盖新场景
- 定期审查测试覆盖范围
- 优化测试执行速度
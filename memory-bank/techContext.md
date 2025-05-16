# 技术背景

## 技术栈
- 主要编程语言：C#
- 核心框架：.NET 6+
- 关键库：
  - Telegram.Bot：Telegram机器人API
  - LiteDB：轻量级NoSQL数据库
  - Lucene.NET：全文搜索引擎
  - PaddleOCR：图片OCR处理
  - Whisper：语音识别(ASR)
  - Ollama：大语言模型集成
  - Stateless：状态机管理库
  - Helper类：通用工具方法集合(BiliHelper, MessageFormatHelper等)
- 进程间通信：共享内存/管道通信
- 消息处理：消息管道式处理

## 开发环境
- 开发工具：Visual Studio/VSCode
- 构建工具：MSBuild
- 测试工具：MSTest

## 技术约束
- 系统限制：需要支持Windows部署
- 性能要求：实时处理群聊消息
- 安全要求：保护用户隐私数据

## 依赖关系
- 外部服务依赖：
  - Telegram Bot API
  - Ollama服务(可选)
- 第三方API：无

## 工具使用模式
- 常用开发命令：
  - dotnet build
  - dotnet run
- Helper类使用：
  - 静态方法直接调用
  - 包含Bilibili相关工具方法
  - 包含消息格式化工具
- 调试技巧：使用AppData/Local/TelegramSearchBot日志
- 部署流程：通过ClickOnce部署

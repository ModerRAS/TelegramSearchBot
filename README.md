# TelegramSearchBot (Rust 版本)

这是 [TelegramSearchBot](https://github.com/ModerRAS/TelegramSearchBot) 的 Rust 重写版本。

## 功能

- ✅ 群聊消息存储与搜索
- ✅ 中文分词搜索 (使用 Tantivy + jieba-rs)
- ✅ SQLite 数据库存储
- 🚧 向量搜索（语义搜索）
- 🚧 AI 对话集成
- 🚧 OCR/ASR 支持

## 项目结构

```
rust-version/
├── Cargo.toml                          # 工作空间配置
├── telegram_search_bot/                # 主应用
│   ├── src/
│   │   ├── main.rs                    # 入口
│   │   ├── lib.rs                     # 库入口
│   │   ├── bot/                       # Bot 核心
│   │   ├── controller/                # 控制器（命令处理）
│   │   ├── database.rs                # 数据库层
│   │   └── service/                   # 服务层
│   └── Cargo.toml
├── telegram_search_bot_common/         # 通用模块
│   ├── src/
│   │   ├── lib.rs
│   │   ├── config.rs                  # 配置管理
│   │   ├── error.rs                   # 错误类型
│   │   └── model/                     # 数据模型
│   └── Cargo.toml
└── telegram_search_bot_search/         # 搜索模块
    ├── src/
    │   ├── lib.rs
    │   ├── manager.rs                 # 搜索管理器
    │   ├── tokenizer.rs               # 中文分词器
    │   └── error.rs
    └── Cargo.toml
```

## 快速开始

### 1. 安装 Rust

```bash
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
```

### 2. 配置

创建配置文件（Windows: `%LOCALAPPDATA%\TelegramSearchBot\Config.json`，Linux/macOS: `~/.local/share/TelegramSearchBot/Config.json`）：

```json
{
  "BaseUrl": "https://api.telegram.org",
  "BotToken": "your-bot-token",
  "AdminId": 123456789,
  "EnableAutoOCR": false,
  "EnableAutoASR": false,
  "IsLocalAPI": false,
  "SameServer": false,
  "TaskDelayTimeout": 1000,
  "OllamaModelName": "qwen2.5:72b-instruct-q2_K",
  "EnableVideoASR": false,
  "EnableOpenAI": false,
  "OpenAIModelName": "gpt-4o",
  "MaxToolCycles": 25
}
```

### 3. 构建和运行

```bash
cd rust-version
cargo build --release
cargo run --release
```

## 使用方法

### 搜索命令

- `搜索 关键词` - 使用倒排索引搜索
- `向量搜索 问题描述` - 使用语义搜索
- `语法搜索 查询语句` - 使用高级语法搜索
- `/search 关键词` - 英文命令搜索
- `/vector 问题描述` - 英文命令向量搜索

## 与 C# 版本的对比

| 功能 | C# 版本 | Rust 版本 |
|------|---------|-----------|
| 搜索引擎 | Lucene.NET | Tantivy |
| 中文分词 | SmartChineseAnalyzer | jieba-rs |
| 数据库 | EF Core + SQLite | SQLx + SQLite |
| Telegram SDK | Telegram.Bot | teloxide |
| 向量搜索 | Faiss.NET | (计划中) |
| AI 集成 | OpenAI/Ollama | (计划中) |
| OCR | PaddleOCR | (计划中) |
| ASR | Whisper | (计划中) |

## 开发

### 运行测试

```bash
cargo test
```

### 代码格式化

```bash
cargo fmt
```

### 代码检查

```bash
cargo clippy
```

## 许可证

MIT License

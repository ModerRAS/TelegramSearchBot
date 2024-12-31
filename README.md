# TelegramSearchBot
自用群聊消息搜索机器人

![Build Status](https://github.com/ModerRAS/TelegramSearchBot/actions/workflows/push.yml/badge.svg)
## 功能列表
1. 群聊消息存储并支持中文分词搜索
1. 群聊消息中图片自动下载并OCR后存储
1. 发送图片附带Caption内容为`打印`时自动OCR文本并回复
1. 群聊中的语音及音频文件及视频及视频文件自动语音识别并回复（短文本回复消息，长文本回复文件）
1. 支持使用Ollama将大语言模型接入，at此bot即可获得回复

## 食用方法

### 安装
#### Windows版使用
1. 安装[软件本体](https://clickonce.miaostay.com/TelegramSearchBot/Publish.html)
2. 运行一次让其自动崩溃，然后打开`AppData/Local/TelegramSearchBot`目录放入`Config.json`配置文件，格式如下，其他功能开关参考`Env.cs`

```
{
"BaseUrl": "http://127.0.0.1:8081",
"BotToken": "your-bot-token",
"AdminId": your-user-id-pure-number,
"EnableAutoOCR": true,
"EnableAutoASR": true,
"IsLocalAPI": true,
"SameServer": true,
"TaskDelayTimeout": 1000,
"OllamaHost": "http://127.0.0.1:11434"
}
```

3. 启动软件本体

### 搜索

1. 去找BotFather创建一个Bot
2. 设置Bot的Group Privacy为disabled
3. 将该Bot加入群聊
4. 输入`搜索 + 空格 + 搜索关键字`，如`搜索 食用方法`

#### 在群聊中

返回该群聊中符合关键字的选项

#### 私聊Bot

返回该Bot所在的所有群聊中 发送者在的群 的所有符合关键字的选项

## 工作方式
读取群聊消息，然后放入LiteDB和Lucene中，然后通过Lucene进行搜索

## 额外功能
可以使用私有搭建的`Telegram Bot API`配合使用，只需要参考`docker-compose.yml`和`.env.example`配置好所需要的参数即可
## License
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FModerRAS%2FTelegramSearchBot.svg?type=large)](https://app.fossa.com/projects/git%2Bgithub.com%2FModerRAS%2FTelegramSearchBot?ref=badge_large)

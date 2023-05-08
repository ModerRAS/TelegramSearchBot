# TelegramSearchBot
自用群聊消息搜索机器人

![Build Status](https://github.com/ModerRAS/TelegramSearchBot/actions/workflows/push.yml/badge.svg)
## 食用方法

### 安装

1. 下载本仓库中的`docker-compose.yml`、 `.env.example`~~和`sonic.cfg`~~
2. 重命名`.env.example`为`.env`
3. 修改`.env`中的BotToken以及其他选项
4. 输入`docker-compose up -d`来启动

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

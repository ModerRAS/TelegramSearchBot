# TelegramSearchBot
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FModerRAS%2FTelegramSearchBot.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2FModerRAS%2FTelegramSearchBot?ref=badge_shield)

自用群聊消息搜索机器人

## 食用方法

### 安装

1. 下载本仓库中的`docker-compose.yml`和`.env.example`
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

### 数据导入
1. 使用 Telegram Desktop 导出对话历史
2. 使用 MessageExporter 导出为json文档
3. 将该文档上传至任一文本托管网站
4. 在Bot中输入`导入 + 空格 + url`（只有管理员有用，其他人发的自动忽略）
5. 等一会（不会有成功提示的所以不等也可以）

tips： 因为导入的时候顺便检查数据库中有没有实在太浪费时间了，所以我就不加检查了，导入的时候小心别多导入了几遍。

## 工作方式
使用sql的like来进行搜索的，所以不需要提前建索引什么的。不过要提前给机器人建立一个空的数据库（docker compose中已经自动给你建好了）


## License
[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FModerRAS%2FTelegramSearchBot.svg?type=large)](https://app.fossa.com/projects/git%2Bgithub.com%2FModerRAS%2FTelegramSearchBot?ref=badge_large)
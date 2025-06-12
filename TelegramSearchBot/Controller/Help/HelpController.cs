using System;
using System.Collections.Generic;
using TelegramSearchBot.Interface;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Service.BotAPI;
using System.IO;
using TelegramSearchBot.Model;
using System.Text;
using TelegramSearchBot.Interface.Controller;

namespace TelegramSearchBot.Controller.Help {
    public class HelpController : IOnUpdate
    {
        private readonly ISendMessageService sendMessageService;
        public List<Type> Dependencies => new List<Type>();

        public HelpController(ISendMessageService sendMessageService)
        {
            this.sendMessageService = sendMessageService;
        }

        public async Task ExecuteAsync(PipelineContext p) 
        {
            var e = p.Update;
            if (!string.IsNullOrEmpty(e?.Message?.Text))
            {
                if (e.Message.Text.Trim().Equals("帮助") || 
                    e.Message.Text.Trim().Equals("/help"))
                {
                    // 嵌入的帮助文档内容
                    var helpText = @"# TelegramSearchBot - 用户指令与使用说明

## 一、通用用户指令
1. **搜索内容**
   - 指令格式: `搜索 <关键词>`
   - 功能: 在机器人索引的消息中搜索

## 二、管理员指令
### 1. LLM 模型设置
- **设置模型**: `设置模型 <模型名称>`

### 2. 全局管理员指令
- **群组管理**: `设置管理群`/`取消管理群`
- **Bilibili配置**: `/setbilicookie`, `/getbilicookie`

## 三、自动功能
1. **与LLM对话**: @机器人用户名
2. **Bilibili链接处理**: 自动识别B站链接
3. **图片OCR识别**: 发送图片自动识别";

                    // 生成分层显示的内容
                    var sb = new StringBuilder();
                    var lines = helpText.Split('\n');
                    var indentLevel = 0;
                    
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("###"))
                        {
                            indentLevel = 2;
                            sb.AppendLine(new string(' ', indentLevel * 2) + line.Substring(3).Trim());
                        }
                        else if (line.StartsWith("##"))
                        {
                            indentLevel = 1;
                            sb.AppendLine(new string(' ', indentLevel * 2) + line.Substring(2).Trim());
                        }
                        else if (line.StartsWith("#"))
                        {
                            indentLevel = 0;
                            sb.AppendLine(line.Substring(1).Trim());
                        }
                        else
                        {
                            sb.AppendLine(new string(' ', indentLevel * 2) + line);
                        }
                    }

                    await sendMessageService.SplitAndSendTextMessage(sb.ToString(), e.Message.Chat, e.Message.MessageId);
                }
            }
        }
    }
}

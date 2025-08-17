using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Scriban;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.View
{
    public class WordCloudView : IView
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ISendMessageService _sendMessage;
        
        private DateTime _date;
        private int _userCount;
        private int _messageCount;
        private List<(string Name, int Count)> _topUsers = new List<(string Name, int Count)>();
        private string _period = "";
        private DateTime _startDate;
        private DateTime _endDate;
        
        // IView接口实现
        private long _chatId;
        private int _replyToMessageId;
        private string _text = "";
        private int _count;
        private int _skip;
        private int _take;
        private SearchType _searchType;

        public WordCloudView(ITelegramBotClient botClient, ISendMessageService sendMessage) 
        {
            _botClient = botClient;
            _sendMessage = sendMessage;
        }

        public WordCloudView WithDate(DateTime date)
        {
            _date = date;
            return this;
        }

        public WordCloudView WithUserCount(int userCount)
        {
            _userCount = userCount;
            return this;
        }

        public WordCloudView WithMessageCount(int messageCount)
        {
            _messageCount = messageCount;
            return this;
        }

        public WordCloudView WithTopUsers(List<(string Name, int Count)> topUsers)
        {
            _topUsers = topUsers;
            return this;
        }

        public WordCloudView WithPeriod(string period)
        {
            _period = period;
            return this;
        }

        public WordCloudView WithDateRange(DateTime startDate, DateTime endDate)
        {
            _startDate = startDate;
            _endDate = endDate;
            return this;
        }
        
        // IView接口方法实现
        public IView WithChatId(long chatId)
        {
            _chatId = chatId;
            return this;
        }

        public IView WithReplyTo(int messageId)
        {
            _replyToMessageId = messageId;
            return this;
        }

        public IView WithText(string text)
        {
            _text = text;
            return this;
        }

        public IView WithCount(int count)
        {
            _count = count;
            return this;
        }

        public IView WithSkip(int skip)
        {
            _skip = skip;
            return this;
        }

        public IView WithTake(int take)
        {
            _take = take;
            return this;
        }

        public IView WithSearchType(SearchType searchType)
        {
            _searchType = searchType;
            return this;
        }

        public IView WithMessages(List<TelegramSearchBot.Model.Data.Message> messages)
        {
            // WordCloudView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithTitle(string title)
        {
            // WordCloudView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithHelp()
        {
            // WordCloudView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView DisableNotification(bool disable = true)
        {
            // WordCloudView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithMessage(string message)
        {
            // WordCloudView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        public IView WithOwnerName(string ownerName)
        {
            // WordCloudView不需要此方法，但为了实现接口提供空实现
            return this;
        }

        private const string TemplateString = @"
☁️ {{ date_str }} {{ period }}热门话题 #WordCloud
⏰ 统计周期: {{ start_date_str }} 至 {{ end_date_str }}
🗣️ 本群 {{ user_count }} 位朋友共产生 {{ message_count }} 条发言
🔍 看下有没有你感兴趣的关键词？

活跃用户排行榜：

{{ for user in top_users }}
    {{ if user.index == 0 }}🥇{{ else if user.index == 1 }}🥈{{ else if user.index == 2 }}🥉{{ else }}🎖️{{ end }}{{ user.name }} 贡献: {{ user.count }}
{{ end }}

🎉感谢这些朋友的分享!🎉
";

        public async Task Render()
        {
            // 简化实现：只发送文本消息，不生成图片
            var template = Template.Parse(TemplateString);
            
            // 如果用户少于10个就全部显示，否则只显示前10名
            var displayCount = _topUsers.Count < 10 ? _topUsers.Count : Math.Min(10, _topUsers.Count);
            
            var users = new List<object>();
            for (int i = 0; i < displayCount; i++)
            {
                users.Add(new {
                    index = i,
                    rank = i + 1,  // 排名从1开始
                    name = _topUsers[i].Name,
                    count = _topUsers[i].Count
                });
            }

            var caption = template.Render(new {
                date_str = _date.ToString("MM-dd"),
                period = _period,
                start_date_str = _startDate.ToString("yyyy-MM-dd"),
                end_date_str = _endDate.ToString("yyyy-MM-dd"),
                user_count = _userCount,
                message_count = _messageCount,
                top_users = users
            });

            await _sendMessage.SendTextMessageAsync(caption, _chatId, _replyToMessageId);
        }
    }
}

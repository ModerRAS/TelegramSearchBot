using System;
using System.Collections.Generic;
using Scriban;
using Telegram.Bot;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.View {
    public class WordCloudView : ImageView {
        private DateTime _date;
        private int _userCount;
        private int _messageCount;
        private List<(string Name, int Count)> _topUsers;
        private string _period;
        private DateTime _startDate;
        private DateTime _endDate;

        public WordCloudView(ITelegramBotClient botClient, SendMessage sendMessage)
            : base(botClient, sendMessage) {
        }

        public WordCloudView WithDate(DateTime date) {
            _date = date;
            return this;
        }

        public WordCloudView WithUserCount(int userCount) {
            _userCount = userCount;
            return this;
        }

        public WordCloudView WithMessageCount(int messageCount) {
            _messageCount = messageCount;
            return this;
        }

        public WordCloudView WithTopUsers(List<(string Name, int Count)> topUsers) {
            _topUsers = topUsers;
            return this;
        }

        public WordCloudView WithPeriod(string period) {
            _period = period;
            return this;
        }

        public WordCloudView WithDateRange(DateTime startDate, DateTime endDate) {
            _startDate = startDate;
            _endDate = endDate;
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

        public WordCloudView BuildCaption() {
            var template = Template.Parse(TemplateString);
            var now = DateTime.Now;

            // 如果用户少于10个就全部显示，否则只显示前10名
            var displayCount = _topUsers.Count < 10 ? _topUsers.Count : Math.Min(10, _topUsers.Count);

            var users = new List<object>();
            for (int i = 0; i < displayCount; i++) {
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

            return ( WordCloudView ) WithCaption(caption);
        }
    }
}

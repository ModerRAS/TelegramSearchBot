using Microsoft.EntityFrameworkCore;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Manager;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service {
    public class CheckBanGroupService {
        protected readonly DataDbContext DataContext;
        public string ServiceName => "CheckBanGroupService";

        public CheckBanGroupService(DataDbContext context) {
            DataContext = context;
        }
        public async Task<bool> CheckBlacklist(long Id) {
            var IsBlacklist = await (from s in DataContext.GroupData
                              where s.Id == Id
                              select s.IsBlacklist).FirstOrDefaultAsync();

            return IsBlacklist;
        }
        public async Task<string> GetGroupList() {
            var GroupList = await (from s in DataContext.GroupData
                            select s).ToListAsync();
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("|Id|群名|是否黑名单|");
            stringBuilder.AppendLine("|---|---|---|");
            foreach (var group in GroupList) {
                stringBuilder.AppendLine($"|{group.Id}|{group.Title}|{group.IsBlacklist}|");
            }
            return stringBuilder.ToString();
        }
        public async Task BanGroup(long Id) {
            var Group = await (from s in DataContext.GroupData
                               where s.Id == Id
                               select s).FirstOrDefaultAsync();
            Group.IsBlacklist = true;
            DataContext.GroupData.Update(Group);
            await DataContext.SaveChangesAsync();
        }
        public async Task UnBanGroup(long Id) {
            var Group = await (from s in DataContext.GroupData
                               where s.Id == Id
                               select s).FirstOrDefaultAsync();
            Group.IsBlacklist = false;
            DataContext.GroupData.Update(Group);
            await DataContext.SaveChangesAsync();
        }
    }
}

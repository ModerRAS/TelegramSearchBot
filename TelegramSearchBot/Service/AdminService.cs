using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service {
    public class AdminService : IService {
        protected readonly DataDbContext DataContext;
        protected readonly ILogger<AdminService> Logger;
        public string ServiceName => "AdminService";
        public AdminService(ILogger<AdminService> logger, DataDbContext context) { 
            Logger = logger;
            DataContext = context;
        }

        public bool IsGlobalAdmin(long Id) {
            return Id == Env.AdminId;
        }

        public async Task<bool> IsNormalAdmin(long Id) {
            var AdminersGroup = from s in DataContext.GroupSettings
                                where s.IsManagerGroup
                                select s;
            foreach (var Adminer in AdminersGroup) {
                var IsAdmin = from s in DataContext.UsersWithGroup
                              where s.UserId == Id && s.GroupId == Adminer.GroupId
                              select s;
                if (IsAdmin.Any()) { 
                    return true;
                }
            }
            return false;
        }

        public async Task<(bool, string)> ExecuteAsync(long UserId, long ChatId, string Command) {
            if (Command.StartsWith("设置管理群")) {
                if (IsGlobalAdmin(UserId)) {
                    var GroupSettings =  from s in DataContext.GroupSettings
                                        where ChatId == s.GroupId
                                        select s;
                    var GroupSetting = GroupSettings.FirstOrDefault();
                    var PreviousSetting = GroupSetting?.IsManagerGroup;
                    if (GroupSetting != null) { 
                        GroupSetting.IsManagerGroup = true;
                    } else {
                        await DataContext.GroupSettings.AddAsync(new GroupSettings() {
                            GroupId = ChatId,
                            IsManagerGroup = true
                        });
                    }
                    await DataContext.SaveChangesAsync();
                    return (true, $"成功设置管理群，之前为{PreviousSetting}，现在为{true}");
                }
            }
            if (Command.StartsWith("取消管理群")) {
                if (IsGlobalAdmin(UserId)) {
                    var GroupSettings = from s in DataContext.GroupSettings
                                        where ChatId == s.GroupId
                                        select s;
                    var GroupSetting = GroupSettings.FirstOrDefault();
                    var PreviousSetting = GroupSetting?.IsManagerGroup;
                    if (GroupSetting != null) {
                        GroupSetting.IsManagerGroup = false;
                    } else {
                        await DataContext.GroupSettings.AddAsync(new GroupSettings() {
                            GroupId = ChatId,
                            IsManagerGroup = false
                        });
                    }
                    await DataContext.SaveChangesAsync();
                    return (true, $"成功取消管理群，之前为{PreviousSetting}，现在为{false}");
                }
            }
            return (false, string.Empty);
        }
    }
}

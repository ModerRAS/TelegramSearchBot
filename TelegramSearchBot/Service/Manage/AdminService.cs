using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Common; // Added for IAppConfigurationService

namespace TelegramSearchBot.Service.Manage
{
    public class AdminService : IService
    {
        protected readonly DataDbContext DataContext;
        protected readonly ILogger<AdminService> Logger;
        private readonly IAppConfigurationService _appConfigService; // Added
        public string ServiceName => "AdminService";
        public AdminService(ILogger<AdminService> logger, DataDbContext context, IAppConfigurationService appConfigService) // Added appConfigService
        {
            Logger = logger;
            DataContext = context;
            _appConfigService = appConfigService; // Store injected service
        }

        public bool IsGlobalAdmin(long Id)
        {
            return Id == Env.AdminId;
        }

        public async Task<bool> IsNormalAdmin(long Id)
        {
            return await (from u in DataContext.UsersWithGroup
                          where u.UserId == Id &&
                                (from g in DataContext.GroupSettings
                                 where g.IsManagerGroup
                                 select g.GroupId)
                                .Contains(u.GroupId)
                          select u).AnyAsync();
        }

        public async Task<(bool, string)> ExecuteAsync(long UserId, long ChatId, string Command)
        {
            if (Command.StartsWith("设置管理群"))
            {
                if (IsGlobalAdmin(UserId))
                {
                    var GroupSettings = from s in DataContext.GroupSettings
                                        where ChatId == s.GroupId
                                        select s;
                    var GroupSetting = GroupSettings.FirstOrDefault();
                    var PreviousSetting = GroupSetting?.IsManagerGroup;
                    if (GroupSetting != null)
                    {
                        GroupSetting.IsManagerGroup = true;
                    }
                    else
                    {
                        await DataContext.GroupSettings.AddAsync(new Model.Data.GroupSettings()
                        {
                            GroupId = ChatId,
                            IsManagerGroup = true
                        });
                    }
                    await DataContext.SaveChangesAsync();
                    return (true, $"成功设置管理群，之前为{PreviousSetting}，现在为{true}");
                }
            }
            if (Command.StartsWith("取消管理群"))
            {
                if (IsGlobalAdmin(UserId))
                {
                    var GroupSettings = from s in DataContext.GroupSettings
                                        where ChatId == s.GroupId
                                        select s;
                    var GroupSetting = GroupSettings.FirstOrDefault();
                    var PreviousSetting = GroupSetting?.IsManagerGroup;
                    if (GroupSetting != null)
                    {
                        GroupSetting.IsManagerGroup = false;
                    }
                    else
                    {
                        await DataContext.GroupSettings.AddAsync(new Model.Data.GroupSettings()
                        {
                            GroupId = ChatId,
                            IsManagerGroup = false
                        });
                    }
                    await DataContext.SaveChangesAsync();
                    return (true, $"成功取消管理群，之前为{PreviousSetting}，现在为{false}");
                }
            }
            if (Command.StartsWith("/setbilicookie ") || Command.StartsWith("设置B站Cookie "))
            {
                if (IsGlobalAdmin(UserId))
                {
                    var parts = Command.Split(new[] { ' ' }, 2);
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        return (true, "请提供Cookie值。用法: /setbilicookie <你的Cookie字符串>");
                    }
                    string cookieValue = parts[1].Trim();
                    await _appConfigService.SetConfigurationValueAsync(AppConfigurationService.BiliCookieKey, cookieValue);
                    return (true, "BiliCookie 已成功设置。");
                }
                else
                {
                    return (true, "抱歉，只有全局管理员才能设置BiliCookie。");
                }
            }
            if (Command.Equals("/getbilicookie", StringComparison.OrdinalIgnoreCase) || Command.Equals("获取B站Cookie", StringComparison.OrdinalIgnoreCase))
            {
                 if (IsGlobalAdmin(UserId))
                 {
                    string cookieValue = await _appConfigService.GetConfigurationValueAsync(AppConfigurationService.BiliCookieKey);
                    if (!string.IsNullOrWhiteSpace(cookieValue))
                    {
                        // For security, maybe don't show the full cookie, or show a masked version/confirmation it's set.
                        // For now, let's just confirm it's set or not.
                        return (true, $"BiliCookie 当前已设置。长度: {cookieValue.Length} (出于安全考虑，不直接显示完整值)");
                    }
                    else
                    {
                        return (true, "BiliCookie 当前未设置。");
                    }
                 }
                 else
                 {
                     return (true, "抱歉，只有全局管理员才能查看BiliCookie状态。");
                 }
            }
            if (Command.StartsWith("/setbilimaxsize ", StringComparison.OrdinalIgnoreCase) || Command.StartsWith("设置B站最大下载大小 ", StringComparison.OrdinalIgnoreCase))
            {
                if (IsGlobalAdmin(UserId))
                {
                    var parts = Command.Split(new[] { ' ' }, 2);
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        return (true, "请提供大小值 (MB)。用法: /setbilimaxsize <MB数>");
                    }
                    if (int.TryParse(parts[1].Trim(), out int sizeInMB) && sizeInMB > 0)
                    {
                        await _appConfigService.SetConfigurationValueAsync(AppConfigurationService.BiliMaxDownloadSizeMBKey, sizeInMB.ToString());
                        return (true, $"Bilibili视频最大下载大小已成功设置为 {sizeInMB}MB。");
                    }
                    else
                    {
                        return (true, "无效的大小值。请输入一个正整数 (MB)。");
                    }
                }
                else
                {
                    return (true, "抱歉，只有全局管理员才能设置此项。");
                }
            }
            if (Command.Equals("/getbilimaxsize", StringComparison.OrdinalIgnoreCase) || Command.Equals("获取B站最大下载大小", StringComparison.OrdinalIgnoreCase))
            {
                 if (IsGlobalAdmin(UserId))
                 {
                    string configuredSizeMB = await _appConfigService.GetConfigurationValueAsync(AppConfigurationService.BiliMaxDownloadSizeMBKey);
                    if (!string.IsNullOrWhiteSpace(configuredSizeMB) && int.TryParse(configuredSizeMB, out int sizeMB) && sizeMB > 0)
                    {
                        return (true, $"Bilibili视频最大下载大小当前设置为: {sizeMB}MB。");
                    }
                    else
                    {
                        // Fallback to the default value used in BiliMessageController if not explicitly set
                        long defaultMaxFileSizeMB = 48; // Keep this consistent with BiliMessageController's default
                        return (true, $"Bilibili视频最大下载大小当前未在数据库中配置，将使用程序默认值 (当前为 {defaultMaxFileSizeMB}MB)。");
                    }
                 }
                 else
                 {
                     return (true, "抱歉，只有全局管理员才能查看此项设置。");
                 }
            }
            return (false, string.Empty);
        }
    }
}

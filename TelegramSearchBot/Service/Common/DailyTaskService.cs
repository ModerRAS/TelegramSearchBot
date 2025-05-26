using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coravel.Invocable;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.Common
{
    public class DailyTaskService : IInvocable
    {
        private readonly DataDbContext _dbContext;

        public DailyTaskService(DataDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task Invoke()
        {
            Console.WriteLine($"[{DateTime.Now}] 每日7点任务执行");
            return Task.CompletedTask;
        }

        public async Task<List<List<string>>> GetDailyGroupMessagesWithExtensionsAsync()
        {
            var result = new List<List<string>>();
            var yesterday = DateTime.Now.AddDays(-1);

            var groups = await _dbContext.GroupData.ToListAsync();
            foreach (var group in groups)
            {
                var groupMessages = await _dbContext.Messages
                    .Where(m => m.GroupId == group.Id && m.DateTime >= yesterday)
                    .Include(m => m.MessageExtensions)
                    .ToListAsync();

                var groupResults = new List<string>();
                foreach (var message in groupMessages)
                {
                    var extensions = message.MessageExtensions?
                        .Select(e => $"{e.Name}={e.Value}")
                        .ToList() ?? new List<string>();

                    groupResults.Add($"Message: {message.Content} | Extensions: {string.Join(", ", extensions)}");
                }

                if (groupResults.Any())
                {
                    result.Add(groupResults);
                }
            }

            return result;
        }
    }
}

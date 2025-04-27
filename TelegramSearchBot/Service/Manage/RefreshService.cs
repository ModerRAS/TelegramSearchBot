using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Manager;
using System.IO;
using Newtonsoft.Json;
using TelegramSearchBot.Model;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Storage;

namespace TelegramSearchBot.Service.Manage
{
    public class RefreshService : MessageService, IService
    {
        public new string ServiceName => "RefreshService";
        private readonly ILogger<RefreshService> _logger;

        public RefreshService(ILogger<RefreshService> logger, LuceneManager lucene, SendMessage Send, DataDbContext context) : base(logger, lucene, Send, context)
        {
            _logger = logger;
        }

        private async Task RebuildIndex()
        {
            var dirs = new List<string>();
            dirs.AddRange(Directory.GetDirectories(Env.WorkDir, "Index_Data_*"));
            dirs.AddRange(Directory.GetDirectories(Env.WorkDir, "Index_Data"));
            await Send.Log($"找到{dirs.Count}个索引目录，现在开始清空目录");
            foreach (var dir in dirs)
            {
                Directory.Delete(dir, true);
                await Send.Log($"删除了{dir}");
            }
            await Send.Log($"删除完成");
            var Messages = Env.Database.GetCollection<Message>("Messages").FindAll();
            long count = Messages.LongCount();
            await Send.Log($"共{count}条消息，现在开始重建索引");
            lucene.WriteDocuments(Messages.ToList());
            await Send.Log($"重建完成");
        }

        private async Task ImportAll()
        {
            await Send.Log("开始导入数据库内容");
            var importModel = JsonConvert.DeserializeObject<ExportModel>(await File.ReadAllTextAsync("/tmp/export.json"));
            var users = Env.Database.GetCollection<UserWithGroup>("Users");
            users.InsertBulk(importModel.Users);
            var messages = Env.Database.GetCollection<Message>("Messages");
            messages.InsertBulk(importModel.Messages);
            await Send.Log("导入完成");
        }

        private async Task CopyLiteDbToSqlite()
        {
            var Messages = Env.Database.GetCollection<Message>("Messages").FindAll();
            long count = Messages.LongCount();
            await Send.Log($"共{count}条消息，现在开始迁移数据至Sqlite");
            var number = 0;
            foreach (var e in Messages)
            {
                number++;
                if (number % 10000 == 0)
                {
                    await Send.Log($"已迁移{number}条数据");
                }
                var sqliteMessage = from sq in DataContext.Messages
                                    where sq.MessageId == e.MessageId &&
                                          sq.GroupId == e.GroupId &&
                                          sq.Content.Equals(e.Content)
                                    select sq;
                if (sqliteMessage.FirstOrDefault() != null)
                {
                    continue;
                }

                await DataContext.Messages.AddAsync(new Message()
                {
                    DateTime = e.DateTime,
                    Content = e.Content,
                    GroupId = e.GroupId,
                    MessageId = e.MessageId,
                });
                await DataContext.SaveChangesAsync();
            }
            await DataContext.SaveChangesAsync();
            await Send.Log($"已迁移{number}条数据，迁移完成");
        }

        public async Task ExecuteAsync(string Command)
        {
            if (Command.Length == 4 && Command.Equals("重建索引"))
            {
                await RebuildIndex();
            }
            if (Command.Length == 4 && Command.Equals("导入数据"))
            {
                await ImportAll();
            }

            if (Command.Length == 4 && Command.Equals("迁移数据"))
            {
                await CopyLiteDbToSqlite();
            }
        }
    }
}

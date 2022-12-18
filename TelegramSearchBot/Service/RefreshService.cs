using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Manager;
using System.IO;
using Newtonsoft.Json;
using TelegramSearchBot.CommonModel;

namespace TelegramSearchBot.Service {
    public class RefreshService : MessageService, IService {
        public new string ServiceName => "RefreshService";

        public RefreshService(LuceneManager lucene, SendMessage Send) : base(lucene, Send) {
        }

        private async Task RebuildIndex() {
            var dirs = Directory.GetDirectories(Env.WorkDir, "Index_Data_*");
            await Send.Log($"找到{dirs.Length}个索引目录，现在开始清空目录");
            foreach (var dir in dirs) {
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

        private async Task ImportAll() {
            await Send.Log("开始导入数据库内容");
            var importModel = JsonConvert.DeserializeObject<ExportModel>(await File.ReadAllTextAsync("/tmp/export.json"));
            var users = Env.Database.GetCollection<User>("Users");
            users.InsertBulk(importModel.Users);
            var messages = Env.Database.GetCollection<Message>("Messages");
            messages.InsertBulk(importModel.Messages);
            await Send.Log("导入完成");
        }

        public async Task ExecuteAsync(string Command) {
            if (Command.Length == 4 && Command.Equals("重建索引")) {
                await RebuildIndex();
            }
            if (Command.Length == 4 && Command.Equals("导入数据")) {
                await ImportAll();
            }
        }
    }
}

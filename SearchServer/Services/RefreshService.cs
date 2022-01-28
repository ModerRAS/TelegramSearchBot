using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Model;
using System.IO;
using Newtonsoft.Json;
using SearchServer.Manager;
using Microsoft.Extensions.Logging;

namespace SearchServer {
    public class RefreshService : MessagerService {
        private readonly ILogger<RefreshService> _logger;
        private readonly LuceneManager lucene;
        public RefreshService(LuceneManager lucene, ILogger<RefreshService> logger) : base (lucene, logger){
            this._logger = logger;
            this.lucene = lucene;
        }

        private async Task RebuildIndex() {
            var dirs = Directory.GetDirectories(Env.WorkDir, "Index_Data_*");
            _logger.LogInformation($"找到{dirs.Length}个索引目录，现在开始清空目录");
            foreach (var dir in dirs) {
                Directory.Delete(dir, true);
                _logger.LogInformation($"删除了{dir}");
            }
            _logger.LogInformation($"删除完成");
            var Messages = Env.Database.GetCollection<Message>("Messages").FindAll();
            long count = Messages.LongCount();
            _logger.LogInformation($"共{count}条消息，现在开始重建索引");
            lucene.WriteDocuments(Messages.ToList());
            _logger.LogInformation($"重建完成");
        }

        //private async Task ImportAll() {
        //    _logger.LogInformation("开始导入数据库内容");
        //    var importModel = JsonConvert.DeserializeObject<ExportModel>(await File.ReadAllTextAsync("/tmp/export.json"));
        //    var users = Env.Database.GetCollection<User>("Users");
        //    users.InsertBulk(importModel.Users);
        //    var messages = Env.Database.GetCollection<Message>("Messages");
        //    messages.InsertBulk(importModel.Messages);
        //    _logger.LogInformation("导入完成");
        //}

        public async Task ExecuteAsync(Command command) {
            if (command.Message.Length == 4 && command.Message.Equals("重建索引")) {
                await RebuildIndex();
            }
            //if (command.Message.Length == 4 && command.Message.Equals("导入数据")) {
            //    await ImportAll();
            //}
        }
    }
}

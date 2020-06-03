using AngleSharp;
using AngleSharp.Dom;
using TelegramSearchBot.CommonModel;
using Newtonsoft.Json;
using RocksDbSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MessageImporter {
    class Program {
        static string Help = @"
导出数据：
MessageExporter GroupId d:/path/to/chat/export/folder
GroupId： 群聊Id，是个负数
";
        static async Task Main(string[] args) {
            if (args.Length != 2) {
                Console.WriteLine(Help);
                return;
            }
            var model = new ImportModel();
            model.Messages = new Dictionary<long, string>();
            var context = BrowsingContext.New(Configuration.Default);
            model.GroupId = long.Parse(args[0]);
            var files = Directory.GetFiles(args[1]);
            foreach (var file in files) {
                if (!file.EndsWith(".html")) {
                    continue;
                }
                var f = File.ReadAllText(file);
                var document = await context.OpenAsync(req => req.Content(f));
                var historys = document.QuerySelectorAll("div.history");
                var history = historys.FirstOrDefault();
                var messages = history.QuerySelectorAll("div.message");
                //var dict = new Dictionary<int, string>();
                foreach (var message in messages) {
                    try {
                        var id = long.Parse(message.Id.Substring(7));
                        var text = message.QuerySelectorAll("div.text").FirstOrDefault().Text();
                        if (text.Length > 4 && (text.Substring(0, 3).Equals("搜索 ") || text.Substring(0, 3).Equals("共找到"))) continue;
                        if (id % 10000 == 0) {
                            Console.WriteLine($"ID: {id}\t Text: {text}");
                        }
                        model.Messages.Add(id, text.Trim());
                        //wr.Write(Encoding.UTF8.GetBytes($"{id.ToString()}\n{text}\n"));
                        //dict.Add(id, text);
                    } catch (System.ArgumentNullException) {
                        //Console.WriteLine("忽略");
                    }

                }
                File.WriteAllText($"{args[0]}.json", JsonConvert.SerializeObject(model));
            }
        }
    }
}
/*
捋一下基本思路：
先是搜索单个文件里的class是history的div， 
然后所有的子div都是消息， 
取出来这些字div的id去掉message这几个字母就是messageid，
然后这里面的class为body的div里的class为text的div里面的文本就是要拉出来的文本， 
如果没有那就忽略
*/
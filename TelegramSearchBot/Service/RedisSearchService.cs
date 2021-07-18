using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service {
    class RedisSearchService : ISearchService, IService {
        /**
         * 整体想法：
         * 把整个句子拆成单个字符，然后建立一个字符到（群组ID：消息ID）的索引
         * 
         * 搜索的时候将搜索的词拆成字符，每个字符去找对应的群组ID里的消息ID，将找到的ID聚合起来一起正则搜索，用一下yield应该就能做到流式传输了
         * 
         * 主要是追求100%的召回率和比直接SQL Like快的速度
         * 
         */
        public string ServiceName => "RedisSearchService";
        public Task<SearchOption> Search(SearchOption searchOption) {
            throw new NotImplementedException();
        }
    }
}

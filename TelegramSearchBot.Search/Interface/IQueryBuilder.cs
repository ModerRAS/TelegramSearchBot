using System.Collections.Generic;
using Lucene.Net.Index;
using Lucene.Net.Search;

namespace TelegramSearchBot.Search.Interface {
    /// <summary>
    /// 统一的查询构建接口 - 为 Content 与 Ext 字段提供一致的查询处理逻辑。
    /// </summary>
    internal interface IQueryBuilder {
        BooleanQuery BuildQuery(string query, long groupId, IndexReader reader);
        List<string> TokenizeQuery(string query);
    }
}

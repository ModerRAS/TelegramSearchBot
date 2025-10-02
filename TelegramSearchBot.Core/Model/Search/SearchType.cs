namespace TelegramSearchBot.Model.Search {
    public enum SearchType {
        /// <summary>
        /// 倒排索引搜索（Lucene）
        /// </summary>
        InvertedIndex = 0,
        /// <summary>
        /// 向量搜索
        /// </summary>
        Vector = 1,
        /// <summary>
        /// 语法搜索（支持字段指定、排除词等语法）
        /// </summary>
        SyntaxSearch = 2
    }
}

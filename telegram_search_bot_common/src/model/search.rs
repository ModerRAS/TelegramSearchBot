//! 搜索相关数据模型

use serde::{Deserialize, Serialize};

use super::Message;

/// 搜索类型
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SearchType {
    /// 倒排索引搜索 (Lucene/Tantivy)
    InvertedIndex,
    /// 向量搜索 (FAISS)
    Vector,
    /// 语法搜索
    SyntaxSearch,
}

impl Default for SearchType {
    fn default() -> Self {
        Self::InvertedIndex
    }
}

/// 搜索选项
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SearchOption {
    /// 搜索关键词
    pub search: String,
    /// 聊天 ID
    pub chat_id: i64,
    /// 是否是群组
    pub is_group: bool,
    /// 搜索类型
    pub search_type: SearchType,
    /// 跳过数量
    pub skip: usize,
    /// 获取数量
    pub take: usize,
    /// 总数
    pub count: i64,
    /// 搜索结果
    pub messages: Vec<Message>,
    /// 待删除的消息 ID
    pub to_delete: Vec<i64>,
    /// 是否立即删除
    pub to_delete_now: bool,
    /// 回复的消息 ID
    pub reply_to_message_id: i64,
}

impl Default for SearchOption {
    fn default() -> Self {
        Self {
            search: String::new(),
            chat_id: 0,
            is_group: false,
            search_type: SearchType::InvertedIndex,
            skip: 0,
            take: 20,
            count: -1,
            messages: vec![],
            to_delete: vec![],
            to_delete_now: false,
            reply_to_message_id: 0,
        }
    }
}

/// 搜索结果
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SearchResult {
    /// 总数
    pub total: i64,
    /// 消息列表
    pub messages: Vec<Message>,
}

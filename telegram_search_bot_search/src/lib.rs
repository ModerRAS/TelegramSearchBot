//! TelegramSearchBot Search - 搜索引擎模块
//!
//! 使用 Tantivy 实现全文搜索，支持中文分词

pub mod error;
pub mod manager;
pub mod tokenizer;

pub use error::{SearchError, SearchResult};
pub use manager::SearchManager;

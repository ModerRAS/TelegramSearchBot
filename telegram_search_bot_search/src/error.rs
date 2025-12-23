//! 搜索错误类型

use thiserror::Error;

#[derive(Error, Debug)]
pub enum SearchError {
    #[error("索引错误: {0}")]
    Index(String),

    #[error("查询错误: {0}")]
    Query(String),

    #[error("IO 错误: {0}")]
    Io(#[from] std::io::Error),

    #[error("Tantivy 错误: {0}")]
    Tantivy(#[from] tantivy::TantivyError),

    #[error("未知错误: {0}")]
    Unknown(String),
}

pub type SearchResult<T> = std::result::Result<T, SearchError>;

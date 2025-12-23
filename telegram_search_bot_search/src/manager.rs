//! 搜索管理器
//!
//! 类似 C# 版本的 LuceneManager，使用 Tantivy 实现

use std::collections::HashMap;
use std::fs;
use std::path::PathBuf;
use std::sync::{Arc, RwLock};

use tantivy::collector::TopDocs;
use tantivy::query::{BooleanQuery, Occur, Query, QueryParser, TermQuery};
use tantivy::schema::*;
use tantivy::{doc, Index, IndexReader, IndexWriter, ReloadPolicy, TantivyDocument};
use tracing::{debug, info, warn};

use telegram_search_bot_common::config::ENV;
use telegram_search_bot_common::model::MessageDto;

use crate::error::{SearchError, SearchResult};
use crate::tokenizer::ChineseTokenizer;

/// 索引字段名常量
mod fields {
    pub const ID: &str = "id";
    pub const GROUP_ID: &str = "group_id";
    pub const MESSAGE_ID: &str = "message_id";
    pub const DATE_TIME: &str = "date_time";
    pub const FROM_USER_ID: &str = "from_user_id";
    pub const REPLY_TO_USER_ID: &str = "reply_to_user_id";
    pub const REPLY_TO_MESSAGE_ID: &str = "reply_to_message_id";
    pub const CONTENT: &str = "content";
}

/// 搜索管理器
pub struct SearchManager {
    index_dir: PathBuf,
    indexes: RwLock<HashMap<i64, Arc<Index>>>,
    readers: RwLock<HashMap<i64, Arc<IndexReader>>>,
}

impl SearchManager {
    /// 创建新的搜索管理器
    pub fn new() -> SearchResult<Self> {
        let index_dir = ENV.index_dir();
        fs::create_dir_all(&index_dir)?;

        Ok(Self {
            index_dir,
            indexes: RwLock::new(HashMap::new()),
            readers: RwLock::new(HashMap::new()),
        })
    }

    /// 构建索引 Schema
    fn build_schema() -> Schema {
        let mut schema_builder = Schema::builder();

        schema_builder.add_i64_field(fields::ID, STORED | INDEXED);
        schema_builder.add_i64_field(fields::GROUP_ID, STORED | INDEXED);
        schema_builder.add_i64_field(fields::MESSAGE_ID, STORED | INDEXED);
        schema_builder.add_date_field(fields::DATE_TIME, STORED | INDEXED);
        schema_builder.add_i64_field(fields::FROM_USER_ID, STORED | INDEXED);
        schema_builder.add_i64_field(fields::REPLY_TO_USER_ID, STORED);
        schema_builder.add_i64_field(fields::REPLY_TO_MESSAGE_ID, STORED);

        // 内容字段使用中文分词
        let text_options = TextOptions::default()
            .set_indexing_options(
                TextFieldIndexing::default()
                    .set_tokenizer("chinese")
                    .set_index_option(IndexRecordOption::WithFreqsAndPositions),
            )
            .set_stored();
        schema_builder.add_text_field(fields::CONTENT, text_options);

        schema_builder.build()
    }

    /// 获取或创建群组索引
    fn get_or_create_index(&self, group_id: i64) -> SearchResult<Arc<Index>> {
        // 先检查缓存
        if let Some(index) = self.indexes.read().unwrap().get(&group_id) {
            return Ok(Arc::clone(index));
        }

        // 创建索引目录
        let group_index_dir = self.index_dir.join(group_id.to_string());
        fs::create_dir_all(&group_index_dir)?;

        let schema = Self::build_schema();

        // 尝试打开现有索引或创建新索引
        let index = if group_index_dir.join("meta.json").exists() {
            Index::open_in_dir(&group_index_dir)?
        } else {
            Index::create_in_dir(&group_index_dir, schema.clone())?
        };

        // 注册中文分词器
        index
            .tokenizers()
            .register("chinese", ChineseTokenizer);

        let index = Arc::new(index);

        // 缓存索引
        self.indexes
            .write()
            .unwrap()
            .insert(group_id, Arc::clone(&index));

        Ok(index)
    }

    /// 获取索引读取器
    fn get_reader(&self, group_id: i64) -> SearchResult<Arc<IndexReader>> {
        // 先检查缓存
        if let Some(reader) = self.readers.read().unwrap().get(&group_id) {
            return Ok(Arc::clone(reader));
        }

        let index = self.get_or_create_index(group_id)?;

        let reader = index
            .reader_builder()
            .reload_policy(ReloadPolicy::OnCommitWithDelay)
            .try_into()?;

        let reader = Arc::new(reader);

        // 缓存读取器
        self.readers
            .write()
            .unwrap()
            .insert(group_id, Arc::clone(&reader));

        Ok(reader)
    }

    /// 写入单条消息
    pub fn write_document(&self, message: &MessageDto) -> SearchResult<()> {
        let index = self.get_or_create_index(message.group_id)?;
        let schema = index.schema();

        let mut index_writer: IndexWriter = index.writer(50_000_000)?;

        let doc = self.build_document(&schema, message)?;
        index_writer.add_document(doc)?;
        index_writer.commit()?;

        // 清除读取器缓存以便重新加载
        self.readers.write().unwrap().remove(&message.group_id);

        info!(
            "已索引消息: group_id={}, message_id={}",
            message.group_id, message.message_id
        );

        Ok(())
    }

    /// 批量写入消息
    pub fn write_documents(&self, messages: &[MessageDto]) -> SearchResult<()> {
        // 按群组分组
        let mut grouped: HashMap<i64, Vec<&MessageDto>> = HashMap::new();
        for msg in messages {
            grouped.entry(msg.group_id).or_default().push(msg);
        }

        for (group_id, group_messages) in grouped {
            let index = self.get_or_create_index(group_id)?;
            let schema = index.schema();

            let mut index_writer: IndexWriter = index.writer(50_000_000)?;

            for message in &group_messages {
                if message.content.is_empty() {
                    continue;
                }

                match self.build_document(&schema, message) {
                    Ok(doc) => {
                        index_writer.add_document(doc)?;
                    }
                    Err(e) => {
                        warn!(
                            "构建文档失败: group_id={}, message_id={}, error={}",
                            message.group_id, message.message_id, e
                        );
                    }
                }
            }

            index_writer.commit()?;

            // 清除读取器缓存
            self.readers.write().unwrap().remove(&group_id);

            info!(
                "批量索引完成: group_id={}, count={}",
                group_id,
                group_messages.len()
            );
        }

        Ok(())
    }

    /// 构建 Tantivy 文档
    fn build_document(&self, schema: &Schema, message: &MessageDto) -> SearchResult<TantivyDocument> {
        let id_field = schema.get_field(fields::ID).unwrap();
        let group_id_field = schema.get_field(fields::GROUP_ID).unwrap();
        let message_id_field = schema.get_field(fields::MESSAGE_ID).unwrap();
        let date_time_field = schema.get_field(fields::DATE_TIME).unwrap();
        let from_user_id_field = schema.get_field(fields::FROM_USER_ID).unwrap();
        let reply_to_user_id_field = schema.get_field(fields::REPLY_TO_USER_ID).unwrap();
        let reply_to_message_id_field = schema.get_field(fields::REPLY_TO_MESSAGE_ID).unwrap();
        let content_field = schema.get_field(fields::CONTENT).unwrap();

        let datetime = tantivy::DateTime::from_timestamp_secs(message.date_time.timestamp());

        let doc = doc!(
            id_field => message.id,
            group_id_field => message.group_id,
            message_id_field => message.message_id,
            date_time_field => datetime,
            from_user_id_field => message.from_user_id,
            reply_to_user_id_field => message.reply_to_user_id,
            reply_to_message_id_field => message.reply_to_message_id,
            content_field => message.content.clone()
        );

        Ok(doc)
    }

    /// 简单搜索
    pub fn search(
        &self,
        query_str: &str,
        group_id: i64,
        skip: usize,
        take: usize,
    ) -> SearchResult<(i64, Vec<MessageDto>)> {
        let reader = match self.get_reader(group_id) {
            Ok(r) => r,
            Err(_) => return Ok((0, vec![])),
        };

        let index = self.get_or_create_index(group_id)?;
        let schema = index.schema();
        let content_field = schema.get_field(fields::CONTENT).unwrap();
        let group_id_field = schema.get_field(fields::GROUP_ID).unwrap();

        let searcher = reader.searcher();

        // 创建查询解析器
        let query_parser = QueryParser::for_index(&index, vec![content_field]);

        // 解析用户查询
        let content_query: Box<dyn Query> = match query_parser.parse_query(query_str) {
            Ok(q) => q,
            Err(e) => {
                debug!("查询解析失败: {}, 使用模糊匹配", e);
                // 回退到简单匹配
                query_parser
                    .parse_query(&format!("\"{}\"", query_str))
                    .map_err(|e| SearchError::Query(e.to_string()))?
            }
        };

        // 构建复合查询：内容匹配 + 群组过滤
        let group_term = tantivy::Term::from_field_i64(group_id_field, group_id);
        let group_query: Box<dyn Query> = Box::new(TermQuery::new(group_term, IndexRecordOption::Basic));

        let combined_query = BooleanQuery::new(vec![
            (Occur::Must, content_query),
            (Occur::Must, group_query),
        ]);

        // 执行搜索
        let top_docs = searcher.search(
            &combined_query,
            &TopDocs::with_limit(skip + take).and_offset(skip),
        )?;

        // 获取总数
        let total = searcher.search(&combined_query, &tantivy::collector::Count)?;

        // 提取结果
        let mut messages = Vec::new();
        for (_score, doc_address) in top_docs {
            let retrieved_doc: TantivyDocument = searcher.doc(doc_address)?;
            if let Some(msg) = self.doc_to_message_dto(&schema, &retrieved_doc) {
                messages.push(msg);
            }
        }

        Ok((total as i64, messages))
    }

    /// 语法搜索
    pub fn syntax_search(
        &self,
        query_str: &str,
        group_id: i64,
        skip: usize,
        take: usize,
    ) -> SearchResult<(i64, Vec<MessageDto>)> {
        // 目前使用相同实现，可以后续扩展支持更复杂的语法
        self.search(query_str, group_id, skip, take)
    }

    /// 将 Tantivy 文档转换为 MessageDto
    fn doc_to_message_dto(&self, schema: &Schema, doc: &TantivyDocument) -> Option<MessageDto> {
        let id = self.get_i64_value(schema, doc, fields::ID)?;
        let group_id = self.get_i64_value(schema, doc, fields::GROUP_ID)?;
        let message_id = self.get_i64_value(schema, doc, fields::MESSAGE_ID)?;
        let from_user_id = self.get_i64_value(schema, doc, fields::FROM_USER_ID)?;
        let reply_to_user_id = self.get_i64_value(schema, doc, fields::REPLY_TO_USER_ID).unwrap_or(0);
        let reply_to_message_id = self.get_i64_value(schema, doc, fields::REPLY_TO_MESSAGE_ID).unwrap_or(0);
        let content = self.get_text_value(schema, doc, fields::CONTENT)?;

        let date_time_field = schema.get_field(fields::DATE_TIME).ok()?;
        let date_time = doc
            .get_first(date_time_field)
            .and_then(|v| v.as_datetime())
            .map(|dt| {
                chrono::DateTime::from_timestamp(dt.into_timestamp_secs(), 0)
                    .unwrap_or_else(chrono::Utc::now)
            })
            .unwrap_or_else(chrono::Utc::now);

        Some(MessageDto {
            id,
            group_id,
            message_id,
            date_time,
            from_user_id,
            reply_to_user_id,
            reply_to_message_id,
            content,
            message_extensions: vec![],
        })
    }

    fn get_i64_value(&self, schema: &Schema, doc: &TantivyDocument, field_name: &str) -> Option<i64> {
        let field = schema.get_field(field_name).ok()?;
        doc.get_first(field)?.as_i64()
    }

    fn get_text_value(&self, schema: &Schema, doc: &TantivyDocument, field_name: &str) -> Option<String> {
        let field = schema.get_field(field_name).ok()?;
        doc.get_first(field)?.as_str().map(|s| s.to_string())
    }
}

impl Default for SearchManager {
    fn default() -> Self {
        Self::new().expect("无法创建搜索管理器")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Utc;

    #[test]
    fn test_build_schema() {
        let schema = SearchManager::build_schema();
        assert!(schema.get_field(fields::ID).is_ok());
        assert!(schema.get_field(fields::CONTENT).is_ok());
    }
}

//! 搜索服务
//!
//! 类似 C# 版本的 SearchService，封装搜索逻辑

use std::sync::Arc;

use anyhow::Result;
use tracing::{debug, info};

use crate::database::Database;
use telegram_search_bot_common::model::{Message, SearchOption, SearchType};
use telegram_search_bot_search::SearchManager;

/// 搜索服务
pub struct SearchService {
    search_manager: Arc<SearchManager>,
    database: Database,
}

impl SearchService {
    /// 创建新的搜索服务
    pub fn new(search_manager: Arc<SearchManager>, database: Database) -> Self {
        Self {
            search_manager,
            database,
        }
    }

    /// 执行搜索
    pub async fn search(&self, mut option: SearchOption) -> Result<SearchOption> {
        match option.search_type {
            SearchType::InvertedIndex => self.lucene_search(&mut option).await,
            SearchType::Vector => self.vector_search(&mut option).await,
            SearchType::SyntaxSearch => self.syntax_search(&mut option).await,
        }?;

        Ok(option)
    }

    /// 倒排索引搜索（使用 Tantivy）
    async fn lucene_search(&self, option: &mut SearchOption) -> Result<()> {
        if option.is_group {
            // 群组搜索
            let (count, messages) = self.search_manager.search(
                &option.search,
                option.chat_id,
                option.skip,
                option.take,
            )?;

            option.count = count;
            option.messages = messages.into_iter().map(|dto| dto.into()).collect();
        } else {
            // 私聊搜索：搜索用户所在的所有群组
            let user_groups = self.database.get_user_groups(option.chat_id).await?;
            let groups_len = user_groups.len().max(1);

            let mut all_messages = Vec::new();
            let mut total_count = 0i64;

            for group in user_groups {
                let (count, messages) = self.search_manager.search(
                    &option.search,
                    group.group_id,
                    option.skip / groups_len,
                    option.take / groups_len,
                )?;

                total_count += count;
                all_messages.extend(messages.into_iter().map(|dto| -> Message { dto.into() }));
            }

            option.count = total_count;
            option.messages = all_messages;
        }

        debug!(
            "倒排索引搜索完成: query={}, count={}",
            option.search, option.count
        );

        Ok(())
    }

    /// 语法搜索
    async fn syntax_search(&self, option: &mut SearchOption) -> Result<()> {
        if option.is_group {
            let (count, messages) = self.search_manager.syntax_search(
                &option.search,
                option.chat_id,
                option.skip,
                option.take,
            )?;

            option.count = count;
            option.messages = messages.into_iter().map(|dto| dto.into()).collect();
        } else {
            let user_groups = self.database.get_user_groups(option.chat_id).await?;
            let groups_len = user_groups.len().max(1);

            let mut all_messages = Vec::new();
            let mut total_count = 0i64;

            for group in user_groups {
                let (count, messages) = self.search_manager.syntax_search(
                    &option.search,
                    group.group_id,
                    option.skip / groups_len,
                    option.take / groups_len,
                )?;

                total_count += count;
                all_messages.extend(messages.into_iter().map(|dto| -> Message { dto.into() }));
            }

            option.count = total_count;
            option.messages = all_messages;
        }

        debug!(
            "语法搜索完成: query={}, count={}",
            option.search, option.count
        );

        Ok(())
    }

    /// 向量搜索（TODO: 实现 FAISS 向量搜索）
    async fn vector_search(&self, option: &mut SearchOption) -> Result<()> {
        // 目前回退到倒排索引搜索
        // TODO: 实现基于 FAISS 或其他向量数据库的语义搜索
        info!("向量搜索暂未实现，使用倒排索引搜索");
        self.lucene_search(option).await
    }
}

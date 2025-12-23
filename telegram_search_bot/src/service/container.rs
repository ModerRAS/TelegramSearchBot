//! 服务容器
//!
//! 管理所有服务实例，类似 C# 的依赖注入容器

use std::sync::Arc;

use anyhow::Result;
use teloxide::prelude::*;
use teloxide::types::CallbackQuery;
use tracing::info;

use super::SearchService;
use crate::database::Database;
use telegram_search_bot_search::SearchManager;

/// 服务容器
pub struct ServiceContainer {
    /// 数据库
    pub database: Database,
    /// 搜索管理器
    pub search_manager: Arc<SearchManager>,
    /// 搜索服务
    pub search_service: SearchService,
}

impl ServiceContainer {
    /// 创建新的服务容器
    pub async fn new(database: Database) -> Result<Self> {
        info!("初始化服务容器...");

        // 创建搜索管理器
        let search_manager = Arc::new(SearchManager::new()?);

        // 创建搜索服务
        let search_service = SearchService::new(
            Arc::clone(&search_manager),
            database.clone(),
        );

        Ok(Self {
            database,
            search_manager,
            search_service,
        })
    }

    /// 处理回调查询
    pub async fn handle_callback(&self, _bot: &Bot, query: &CallbackQuery) -> Result<()> {
        let data = match &query.data {
            Some(d) => d,
            None => return Ok(()),
        };

        // 解析回调数据
        let parts: Vec<&str> = data.split(':').collect();
        if parts.is_empty() {
            return Ok(());
        }

        match parts[0] {
            "search" => {
                // 翻页搜索: search:type:query:skip
                if parts.len() >= 4 {
                    let search_type = parts[1];
                    let query_str = parts[2];
                    let skip: usize = parts[3].parse().unwrap_or(0);

                    info!("翻页搜索: type={}, query={}, skip={}", search_type, query_str, skip);
                    // TODO: 实现翻页搜索
                }
            }
            "switch" => {
                // 切换搜索类型: switch:type:query
                if parts.len() >= 3 {
                    let search_type = parts[1];
                    let query_str = parts[2];

                    info!("切换搜索类型: type={}, query={}", search_type, query_str);
                    // TODO: 实现切换搜索类型
                }
            }
            _ => {}
        }

        Ok(())
    }
}

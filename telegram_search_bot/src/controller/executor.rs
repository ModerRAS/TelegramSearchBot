//! 控制器执行器
//!
//! 类似 C# 版本的 ControllerExecutor，按依赖顺序执行控制器

use std::sync::Arc;

use anyhow::Result;
use async_trait::async_trait;
use teloxide::prelude::*;
use tracing::{debug, error};

use super::pipeline::PipelineContext;
use super::search::SearchController;
use super::storage::StorageController;
use crate::service::ServiceContainer;

/// 控制器 trait
#[async_trait]
pub trait Controller: Send + Sync {
    /// 控制器名称
    fn name(&self) -> &'static str;

    /// 执行控制器
    async fn execute(&self, ctx: &mut PipelineContext, bot: &Bot) -> Result<()>;
}

/// 控制器执行器
pub struct ControllerExecutor {
    services: Arc<ServiceContainer>,
}

impl ControllerExecutor {
    /// 创建新的控制器执行器
    pub fn new(services: Arc<ServiceContainer>) -> Self {
        Self { services }
    }

    /// 执行所有控制器
    pub async fn execute(&self, ctx: &mut PipelineContext, bot: &Bot) -> Result<()> {
        // 按顺序创建和执行控制器
        // 1. 存储控制器 - 保存消息
        let storage = StorageController::new(Arc::clone(&self.services));
        self.run_controller(&storage, ctx, bot).await;

        // 2. 搜索控制器 - 处理搜索命令
        let search = SearchController::new(Arc::clone(&self.services));
        self.run_controller(&search, ctx, bot).await;

        Ok(())
    }

    /// 运行单个控制器
    async fn run_controller<C: Controller>(&self, controller: &C, ctx: &mut PipelineContext, bot: &Bot) {
        debug!("执行控制器: {}", controller.name());

        if let Err(e) = controller.execute(ctx, bot).await {
            error!("控制器 {} 执行失败: {}", controller.name(), e);
        }
    }
}

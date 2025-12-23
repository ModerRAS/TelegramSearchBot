//! 存储控制器
//!
//! 负责保存消息到数据库和搜索索引

use std::sync::Arc;

use anyhow::Result;
use async_trait::async_trait;
use teloxide::prelude::*;
use tracing::debug;

use super::executor::Controller;
use super::pipeline::PipelineContext;
use crate::service::ServiceContainer;
use telegram_search_bot_common::model::{Message as DataMessage, MessageDto};

/// 存储控制器
pub struct StorageController {
    services: Arc<ServiceContainer>,
}

impl StorageController {
    pub fn new(services: Arc<ServiceContainer>) -> Self {
        Self { services }
    }
}

#[async_trait]
impl Controller for StorageController {
    fn name(&self) -> &'static str {
        "StorageController"
    }

    async fn execute(&self, ctx: &mut PipelineContext, _bot: &Bot) -> Result<()> {
        let msg = &ctx.message;

        // 只处理有文本内容的消息
        let content = match msg.text().or(msg.caption()) {
            Some(text) if !text.is_empty() => text.to_string(),
            _ => return Ok(()),
        };

        // 构建消息数据
        let data_message = DataMessage {
            id: 0, // 自增
            date_time: msg.date.into(),
            group_id: msg.chat.id.0,
            message_id: msg.id.0 as i64,
            from_user_id: msg.from.as_ref().map(|u| u.id.0 as i64).unwrap_or(0),
            reply_to_user_id: msg
                .reply_to_message()
                .and_then(|r| r.from.as_ref())
                .map(|u| u.id.0 as i64)
                .unwrap_or(0),
            reply_to_message_id: msg
                .reply_to_message()
                .map(|r| r.id.0 as i64)
                .unwrap_or(0),
            content: content.clone(),
        };

        // 保存到数据库
        let db_id = self.services.database.save_message(&data_message).await?;
        debug!("消息已保存到数据库: id={}", db_id);

        // 构建 DTO 用于索引
        let message_dto = MessageDto {
            id: db_id,
            date_time: data_message.date_time,
            group_id: data_message.group_id,
            message_id: data_message.message_id,
            from_user_id: data_message.from_user_id,
            reply_to_user_id: data_message.reply_to_user_id,
            reply_to_message_id: data_message.reply_to_message_id,
            content,
            message_extensions: vec![],
        };

        // 添加到搜索索引
        if let Err(e) = self.services.search_manager.write_document(&message_dto) {
            tracing::warn!("消息索引失败: {}", e);
        } else {
            debug!("消息已添加到索引");
        }

        // 记录用户-群组关联
        if ctx.is_group() {
            if let Some(user_id) = ctx.from_user_id() {
                self.services
                    .database
                    .add_user_to_group(user_id, ctx.chat_id())
                    .await?;
            }
        }

        Ok(())
    }
}

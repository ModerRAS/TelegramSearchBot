//! 搜索控制器
//!
//! 处理搜索命令：搜索、向量搜索、语法搜索

use std::sync::Arc;

use anyhow::Result;
use async_trait::async_trait;
use teloxide::prelude::*;
use teloxide::types::{InlineKeyboardButton, InlineKeyboardMarkup, ParseMode, ReplyParameters};
use tracing::info;

use super::executor::Controller;
use super::pipeline::PipelineContext;
use crate::service::ServiceContainer;
use telegram_search_bot_common::model::{SearchOption, SearchType};

/// 搜索控制器
pub struct SearchController {
    services: Arc<ServiceContainer>,
}

impl SearchController {
    pub fn new(services: Arc<ServiceContainer>) -> Self {
        Self { services }
    }

    /// 处理搜索命令
    async fn handle_search(
        &self,
        ctx: &PipelineContext,
        bot: &Bot,
        query: &str,
        search_type: SearchType,
    ) -> Result<()> {
        info!(
            "执行搜索: query={}, type={:?}, chat_id={}",
            query,
            search_type,
            ctx.chat_id()
        );

        // 构建搜索选项
        let search_option = SearchOption {
            search: query.to_string(),
            chat_id: ctx.chat_id(),
            is_group: ctx.is_group(),
            search_type,
            skip: 0,
            take: 20,
            count: -1,
            messages: vec![],
            to_delete: vec![],
            to_delete_now: false,
            reply_to_message_id: ctx.message_id() as i64,
        };

        // 执行搜索
        let result = self.services.search_service.search(search_option).await?;

        // 构建回复消息
        let response = self.format_search_result(&result, query);

        // 构建按钮
        let mut buttons = Vec::new();

        // 下一页按钮
        if result.count > 20 {
            let callback_data = format!("search:{}:{}:20", search_type_to_str(search_type), query);
            buttons.push(vec![InlineKeyboardButton::callback("下一页", callback_data)]);
        }

        // 切换搜索类型按钮
        let alt_type = match search_type {
            SearchType::InvertedIndex => SearchType::Vector,
            SearchType::Vector => SearchType::SyntaxSearch,
            SearchType::SyntaxSearch => SearchType::InvertedIndex,
        };
        let alt_type_name = match alt_type {
            SearchType::InvertedIndex => "倒排索引",
            SearchType::Vector => "向量搜索",
            SearchType::SyntaxSearch => "语法搜索",
        };
        let switch_callback = format!("switch:{}:{}", search_type_to_str(alt_type), query);
        buttons.push(vec![InlineKeyboardButton::callback(
            format!("切换到{}", alt_type_name),
            switch_callback,
        )]);

        let keyboard = InlineKeyboardMarkup::new(buttons);

        // 发送回复
        bot.send_message(ChatId(ctx.chat_id()), response)
            .parse_mode(ParseMode::Html)
            .reply_parameters(ReplyParameters::new(ctx.message.id))
            .reply_markup(keyboard)
            .await?;

        Ok(())
    }

    /// 格式化搜索结果
    fn format_search_result(&self, result: &SearchOption, query: &str) -> String {
        if result.messages.is_empty() {
            return format!("🔍 未找到与 <b>{}</b> 相关的消息", html_escape(query));
        }

        let mut response = format!(
            "🔍 搜索 <b>{}</b> 的结果 (共 {} 条):\n\n",
            html_escape(query),
            result.count
        );

        for (i, msg) in result.messages.iter().take(10).enumerate() {
            let preview = if msg.content.len() > 100 {
                format!("{}...", &msg.content[..100])
            } else {
                msg.content.clone()
            };

            response.push_str(&format!(
                "{}. [{}] {}\n",
                i + 1,
                msg.date_time.format("%m-%d %H:%M"),
                html_escape(&preview)
            ));
        }

        if result.messages.len() > 10 {
            response.push_str(&format!("\n... 还有 {} 条结果", result.messages.len() - 10));
        }

        response
    }
}

#[async_trait]
impl Controller for SearchController {
    fn name(&self) -> &'static str {
        "SearchController"
    }

    async fn execute(&self, ctx: &mut PipelineContext, bot: &Bot) -> Result<()> {
        let text = match ctx.text() {
            Some(t) => t,
            None => return Ok(()),
        };

        // 检查搜索命令
        if text.starts_with("搜索 ") && text.len() > 3 {
            let query = &text[7..]; // "搜索 " 是 7 字节（包含空格）
            self.handle_search(ctx, bot, query.trim(), SearchType::InvertedIndex)
                .await?;
            ctx.mark_handled();
        } else if text.starts_with("向量搜索 ") && text.len() > 13 {
            let query = &text[13..]; // "向量搜索 " 是 13 字节
            self.handle_search(ctx, bot, query.trim(), SearchType::Vector)
                .await?;
            ctx.mark_handled();
        } else if text.starts_with("语法搜索 ") && text.len() > 13 {
            let query = &text[13..]; // "语法搜索 " 是 13 字节
            self.handle_search(ctx, bot, query.trim(), SearchType::SyntaxSearch)
                .await?;
            ctx.mark_handled();
        } else if text.starts_with("/search ") && text.len() > 8 {
            let query = &text[8..];
            self.handle_search(ctx, bot, query.trim(), SearchType::InvertedIndex)
                .await?;
            ctx.mark_handled();
        } else if text.starts_with("/vector ") && text.len() > 8 {
            let query = &text[8..];
            self.handle_search(ctx, bot, query.trim(), SearchType::Vector)
                .await?;
            ctx.mark_handled();
        }

        Ok(())
    }
}

/// 搜索类型转字符串
fn search_type_to_str(t: SearchType) -> &'static str {
    match t {
        SearchType::InvertedIndex => "idx",
        SearchType::Vector => "vec",
        SearchType::SyntaxSearch => "syn",
    }
}

/// HTML 转义
fn html_escape(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
}

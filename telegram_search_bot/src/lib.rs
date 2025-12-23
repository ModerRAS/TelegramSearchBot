//! TelegramSearchBot - Telegram 群聊消息搜索机器人
//!
//! 功能:
//! - 群聊消息存储与中文分词搜索
//! - 向量搜索（语义搜索）
//! - AI 对话集成

pub mod bot;
pub mod controller;
pub mod database;
pub mod service;

use anyhow::Result;
use tracing::info;
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

use telegram_search_bot_common::config::ENV;

/// 初始化日志系统
pub fn init_logging() {
    let log_dir = ENV.log_dir();
    std::fs::create_dir_all(&log_dir).expect("无法创建日志目录");

    // 控制台输出
    let console_layer = tracing_subscriber::fmt::layer()
        .with_target(true)
        .with_thread_ids(true)
        .with_file(true)
        .with_line_number(true);

    // 文件输出
    let file_appender = tracing_appender::rolling::daily(&log_dir, "telegram_search_bot.log");
    let (non_blocking, _guard) = tracing_appender::non_blocking(file_appender);
    let file_layer = tracing_subscriber::fmt::layer()
        .with_writer(non_blocking)
        .with_ansi(false);

    tracing_subscriber::registry()
        .with(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "info,sqlx=warn".into()),
        )
        .with(console_layer)
        .with(file_layer)
        .init();

    info!("日志系统初始化完成");
}

/// 确保必要目录存在
pub fn ensure_directories() -> Result<()> {
    std::fs::create_dir_all(ENV.log_dir())?;
    std::fs::create_dir_all(ENV.index_dir())?;
    std::fs::create_dir_all(ENV.faiss_index_dir())?;
    Ok(())
}

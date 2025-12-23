//! TelegramSearchBot 主入口

use anyhow::Result;
use tracing::info;

use telegram_search_bot::bot::BotRunner;
use telegram_search_bot::database::Database;
use telegram_search_bot::{ensure_directories, init_logging};
use telegram_search_bot_common::config::ENV;

#[tokio::main]
async fn main() -> Result<()> {
    // 初始化日志
    init_logging();

    info!("TelegramSearchBot 启动中...");
    info!("工作目录: {:?}", ENV.work_dir);

    // 确保必要目录存在
    ensure_directories()?;

    // 初始化数据库
    let database = Database::new().await?;
    database.migrate().await?;
    info!("数据库初始化完成");

    // 启动机器人
    let bot_runner = BotRunner::new(database).await?;
    info!("机器人初始化完成，开始接收消息...");

    bot_runner.run().await?;

    Ok(())
}

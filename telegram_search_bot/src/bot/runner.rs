//! Bot 运行器

use std::sync::Arc;

use anyhow::Result;
use teloxide::prelude::*;
use tracing::{error, info};

use crate::controller::{ControllerExecutor, PipelineContext};
use crate::database::Database;
use crate::service::ServiceContainer;
use telegram_search_bot_common::config::ENV;

/// Bot 运行器
pub struct BotRunner {
    bot: Bot,
    services: Arc<ServiceContainer>,
}

impl BotRunner {
    /// 创建新的 Bot 运行器
    pub async fn new(database: Database) -> Result<Self> {
        let bot_token = &ENV.config.bot_token;
        if bot_token.is_empty() {
            anyhow::bail!("BotToken 未配置，请在 Config.json 中设置");
        }

        // 配置 Bot
        let bot = if ENV.config.is_local_api {
            Bot::from_env_with_client(
                reqwest::Client::builder()
                    .timeout(std::time::Duration::from_secs(300))
                    .build()?,
            )
            .set_api_url(reqwest::Url::parse(&ENV.config.base_url)?)
        } else {
            Bot::new(bot_token)
        };

        // 获取 Bot 信息
        let me = bot.get_me().await?;
        info!("Bot 信息: @{} (ID: {})", me.username(), me.id);
        ENV.set_bot_id(me.id.0 as i64);

        // 创建服务容器
        let services = Arc::new(ServiceContainer::new(database).await?);

        Ok(Self { bot, services })
    }

    /// 运行 Bot
    pub async fn run(self) -> Result<()> {
        info!("开始接收消息...");

        let handler = dptree::entry()
            .branch(Update::filter_message().endpoint(Self::handle_message))
            .branch(Update::filter_callback_query().endpoint(Self::handle_callback));

        Dispatcher::builder(self.bot, handler)
            .dependencies(dptree::deps![self.services])
            .enable_ctrlc_handler()
            .build()
            .dispatch()
            .await;

        Ok(())
    }

    /// 处理消息
    async fn handle_message(
        bot: Bot,
        msg: Message,
        services: Arc<ServiceContainer>,
    ) -> ResponseResult<()> {
        // 创建管道上下文
        let mut context = PipelineContext::new(msg.clone());

        // 执行控制器
        let executor = ControllerExecutor::new(services);
        if let Err(e) = executor.execute(&mut context, &bot).await {
            error!("处理消息时出错: {}", e);
        }

        Ok(())
    }

    /// 处理回调查询
    async fn handle_callback(
        bot: Bot,
        query: CallbackQuery,
        services: Arc<ServiceContainer>,
    ) -> ResponseResult<()> {
        if let Some(data) = &query.data {
            info!("收到回调: {}", data);

            // 处理翻页等回调
            if let Err(e) = services.handle_callback(&bot, &query).await {
                error!("处理回调时出错: {}", e);
            }
        }

        // 应答回调
        bot.answer_callback_query(&query.id).await?;

        Ok(())
    }
}

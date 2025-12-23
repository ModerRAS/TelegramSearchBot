//! 管道上下文
//!
//! 类似 C# 版本的 PipelineContext

use std::any::Any;
use std::collections::HashMap;
use teloxide::types::Message;

/// 管道上下文，在控制器之间传递数据
pub struct PipelineContext {
    /// 原始 Telegram 消息
    pub message: Message,
    /// 管道缓存，用于控制器间数据传递
    pub cache: HashMap<String, Box<dyn Any + Send + Sync>>,
    /// 是否已处理（如果已处理，后续控制器可以跳过）
    pub handled: bool,
}

impl PipelineContext {
    /// 创建新的管道上下文
    pub fn new(message: Message) -> Self {
        Self {
            message,
            cache: HashMap::new(),
            handled: false,
        }
    }

    /// 设置缓存值
    pub fn set<T: Any + Send + Sync>(&mut self, key: &str, value: T) {
        self.cache.insert(key.to_string(), Box::new(value));
    }

    /// 获取缓存值
    pub fn get<T: Any + Send + Sync + Clone>(&self, key: &str) -> Option<T> {
        self.cache
            .get(key)
            .and_then(|v| v.downcast_ref::<T>())
            .cloned()
    }

    /// 标记为已处理
    pub fn mark_handled(&mut self) {
        self.handled = true;
    }

    /// 检查是否是群组消息
    pub fn is_group(&self) -> bool {
        self.message.chat.id.0 < 0
    }

    /// 获取聊天 ID
    pub fn chat_id(&self) -> i64 {
        self.message.chat.id.0
    }

    /// 获取消息 ID
    pub fn message_id(&self) -> i32 {
        self.message.id.0
    }

    /// 获取发送者 ID
    pub fn from_user_id(&self) -> Option<i64> {
        self.message.from.as_ref().map(|u| u.id.0 as i64)
    }

    /// 获取消息文本
    pub fn text(&self) -> Option<&str> {
        self.message.text()
    }
}

//! 消息数据模型

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

/// 消息数据结构
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Message {
    /// 数据库主键
    pub id: i64,
    /// 消息时间
    pub date_time: DateTime<Utc>,
    /// 群组/聊天 ID
    pub group_id: i64,
    /// 消息 ID
    pub message_id: i64,
    /// 发送者用户 ID
    pub from_user_id: i64,
    /// 回复的用户 ID
    pub reply_to_user_id: i64,
    /// 回复的消息 ID
    pub reply_to_message_id: i64,
    /// 消息内容
    pub content: String,
}

impl Default for Message {
    fn default() -> Self {
        Self {
            id: 0,
            date_time: Utc::now(),
            group_id: 0,
            message_id: 0,
            from_user_id: 0,
            reply_to_user_id: 0,
            reply_to_message_id: 0,
            content: String::new(),
        }
    }
}

/// 消息扩展数据
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MessageExtension {
    pub id: i64,
    pub message_id: i64,
    pub name: String,
    pub value: String,
}

/// 消息 DTO，用于搜索索引
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MessageDto {
    pub id: i64,
    pub date_time: DateTime<Utc>,
    pub group_id: i64,
    pub message_id: i64,
    pub from_user_id: i64,
    pub reply_to_user_id: i64,
    pub reply_to_message_id: i64,
    pub content: String,
    pub message_extensions: Vec<MessageExtensionDto>,
}

/// 消息扩展 DTO
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MessageExtensionDto {
    pub name: String,
    pub value: String,
}

impl From<Message> for MessageDto {
    fn from(msg: Message) -> Self {
        Self {
            id: msg.id,
            date_time: msg.date_time,
            group_id: msg.group_id,
            message_id: msg.message_id,
            from_user_id: msg.from_user_id,
            reply_to_user_id: msg.reply_to_user_id,
            reply_to_message_id: msg.reply_to_message_id,
            content: msg.content,
            message_extensions: vec![],
        }
    }
}

impl From<MessageDto> for Message {
    fn from(dto: MessageDto) -> Self {
        Self {
            id: dto.id,
            date_time: dto.date_time,
            group_id: dto.group_id,
            message_id: dto.message_id,
            from_user_id: dto.from_user_id,
            reply_to_user_id: dto.reply_to_user_id,
            reply_to_message_id: dto.reply_to_message_id,
            content: dto.content,
        }
    }
}

/// 用户与群组关联
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserWithGroup {
    pub id: i64,
    pub user_id: i64,
    pub group_id: i64,
}

/// 用户数据
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserData {
    pub id: i64,
    pub user_id: i64,
    pub username: Option<String>,
    pub first_name: Option<String>,
    pub last_name: Option<String>,
}

/// 群组数据
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GroupData {
    pub id: i64,
    pub group_id: i64,
    pub title: Option<String>,
}

/// 群组设置
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GroupSettings {
    pub id: i64,
    pub group_id: i64,
    pub enable_search: bool,
    pub enable_ai: bool,
    pub enable_ocr: bool,
    pub enable_asr: bool,
}

impl Default for GroupSettings {
    fn default() -> Self {
        Self {
            id: 0,
            group_id: 0,
            enable_search: true,
            enable_ai: true,
            enable_ocr: false,
            enable_asr: false,
        }
    }
}

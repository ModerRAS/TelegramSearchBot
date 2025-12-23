//! 数据库模块

use anyhow::Result;
use sqlx::sqlite::{SqliteConnectOptions, SqlitePoolOptions};
use sqlx::{Pool, Sqlite};
use std::str::FromStr;
use tracing::info;

use telegram_search_bot_common::config::ENV;
use telegram_search_bot_common::model::*;

/// 数据库连接池
#[derive(Clone)]
pub struct Database {
    pool: Pool<Sqlite>,
}

impl Database {
    /// 创建新的数据库连接
    pub async fn new() -> Result<Self> {
        let db_path = ENV.database_path();
        let db_url = format!("sqlite:{}?mode=rwc", db_path.display());

        info!("连接数据库: {}", db_url);

        let options = SqliteConnectOptions::from_str(&db_url)?
            .create_if_missing(true)
            .journal_mode(sqlx::sqlite::SqliteJournalMode::Wal)
            .synchronous(sqlx::sqlite::SqliteSynchronous::Normal);

        let pool = SqlitePoolOptions::new()
            .max_connections(5)
            .connect_with(options)
            .await?;

        Ok(Self { pool })
    }

    /// 执行数据库迁移
    pub async fn migrate(&self) -> Result<()> {
        info!("执行数据库迁移...");

        // 创建消息表
        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                date_time TEXT NOT NULL,
                group_id INTEGER NOT NULL,
                message_id INTEGER NOT NULL,
                from_user_id INTEGER NOT NULL,
                reply_to_user_id INTEGER DEFAULT 0,
                reply_to_message_id INTEGER DEFAULT 0,
                content TEXT NOT NULL
            )
            "#,
        )
        .execute(&self.pool)
        .await?;

        // 创建消息索引
        sqlx::query(
            r#"
            CREATE INDEX IF NOT EXISTS idx_messages_group_id ON messages(group_id)
            "#,
        )
        .execute(&self.pool)
        .await?;

        sqlx::query(
            r#"
            CREATE INDEX IF NOT EXISTS idx_messages_message_id ON messages(group_id, message_id)
            "#,
        )
        .execute(&self.pool)
        .await?;

        // 创建用户群组关联表
        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS users_with_group (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                group_id INTEGER NOT NULL,
                UNIQUE(user_id, group_id)
            )
            "#,
        )
        .execute(&self.pool)
        .await?;

        // 创建用户数据表
        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS user_data (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL UNIQUE,
                username TEXT,
                first_name TEXT,
                last_name TEXT
            )
            "#,
        )
        .execute(&self.pool)
        .await?;

        // 创建群组数据表
        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS group_data (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                group_id INTEGER NOT NULL UNIQUE,
                title TEXT
            )
            "#,
        )
        .execute(&self.pool)
        .await?;

        // 创建群组设置表
        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS group_settings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                group_id INTEGER NOT NULL UNIQUE,
                enable_search INTEGER DEFAULT 1,
                enable_ai INTEGER DEFAULT 1,
                enable_ocr INTEGER DEFAULT 0,
                enable_asr INTEGER DEFAULT 0
            )
            "#,
        )
        .execute(&self.pool)
        .await?;

        // 创建消息扩展表
        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS message_extensions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                value TEXT NOT NULL,
                FOREIGN KEY (message_id) REFERENCES messages(id)
            )
            "#,
        )
        .execute(&self.pool)
        .await?;

        // 创建搜索页面缓存表
        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS search_page_cache (
                id TEXT PRIMARY KEY,
                chat_id INTEGER NOT NULL,
                search_type TEXT NOT NULL,
                search_query TEXT NOT NULL,
                skip INTEGER NOT NULL,
                take INTEGER NOT NULL,
                created_at TEXT NOT NULL
            )
            "#,
        )
        .execute(&self.pool)
        .await?;

        info!("数据库迁移完成");
        Ok(())
    }

    /// 保存消息
    pub async fn save_message(&self, message: &Message) -> Result<i64> {
        let result = sqlx::query(
            r#"
            INSERT INTO messages (date_time, group_id, message_id, from_user_id, reply_to_user_id, reply_to_message_id, content)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            "#,
        )
        .bind(message.date_time.to_rfc3339())
        .bind(message.group_id)
        .bind(message.message_id)
        .bind(message.from_user_id)
        .bind(message.reply_to_user_id)
        .bind(message.reply_to_message_id)
        .bind(&message.content)
        .execute(&self.pool)
        .await?;

        Ok(result.last_insert_rowid())
    }

    /// 根据群组和消息 ID 获取消息
    pub async fn get_message(&self, group_id: i64, message_id: i64) -> Result<Option<Message>> {
        let row: Option<sqlx::sqlite::SqliteRow> = sqlx::query(
            r#"
            SELECT id, date_time, group_id, message_id, from_user_id, reply_to_user_id, reply_to_message_id, content
            FROM messages
            WHERE group_id = ? AND message_id = ?
            "#,
        )
        .bind(group_id)
        .bind(message_id)
        .fetch_optional(&self.pool)
        .await?;

        match row {
            Some(r) => Ok(Some(row_to_message(&r)?)),
            None => Ok(None),
        }
    }

    /// 获取用户所在的所有群组
    pub async fn get_user_groups(&self, user_id: i64) -> Result<Vec<UserWithGroup>> {
        
        
        let rows: Vec<sqlx::sqlite::SqliteRow> = sqlx::query(
            r#"
            SELECT id, user_id, group_id
            FROM users_with_group
            WHERE user_id = ?
            "#,
        )
        .bind(user_id)
        .fetch_all(&self.pool)
        .await?;

        let mut groups = Vec::new();
        for row in rows {
            groups.push(row_to_user_with_group(&row)?);
        }
        Ok(groups)
    }

    /// 添加用户群组关联
    pub async fn add_user_to_group(&self, user_id: i64, group_id: i64) -> Result<()> {
        sqlx::query(
            r#"
            INSERT OR IGNORE INTO users_with_group (user_id, group_id)
            VALUES (?, ?)
            "#,
        )
        .bind(user_id)
        .bind(group_id)
        .execute(&self.pool)
        .await?;

        Ok(())
    }

    /// 获取群组设置
    pub async fn get_group_settings(&self, group_id: i64) -> Result<Option<GroupSettings>> {
        let row: Option<sqlx::sqlite::SqliteRow> = sqlx::query(
            r#"
            SELECT id, group_id, enable_search, enable_ai, enable_ocr, enable_asr
            FROM group_settings
            WHERE group_id = ?
            "#,
        )
        .bind(group_id)
        .fetch_optional(&self.pool)
        .await?;

        match row {
            Some(r) => Ok(Some(row_to_group_settings(&r)?)),
            None => Ok(None),
        }
    }

    /// 保存或更新群组设置
    pub async fn save_group_settings(&self, settings: &GroupSettings) -> Result<()> {
        sqlx::query(
            r#"
            INSERT INTO group_settings (group_id, enable_search, enable_ai, enable_ocr, enable_asr)
            VALUES (?, ?, ?, ?, ?)
            ON CONFLICT(group_id) DO UPDATE SET
                enable_search = excluded.enable_search,
                enable_ai = excluded.enable_ai,
                enable_ocr = excluded.enable_ocr,
                enable_asr = excluded.enable_asr
            "#,
        )
        .bind(settings.group_id)
        .bind(settings.enable_search)
        .bind(settings.enable_ai)
        .bind(settings.enable_ocr)
        .bind(settings.enable_asr)
        .execute(&self.pool)
        .await?;

        Ok(())
    }

    /// 获取数据库连接池
    pub fn pool(&self) -> &Pool<Sqlite> {
        &self.pool
    }
}

// 内部消息类型转换辅助函数
fn row_to_message(row: &sqlx::sqlite::SqliteRow) -> std::result::Result<Message, sqlx::Error> {
    use sqlx::Row;

    let date_time_str: String = row.try_get("date_time")?;
    let date_time = chrono::DateTime::parse_from_rfc3339(&date_time_str)
        .map(|dt| dt.with_timezone(&chrono::Utc))
        .unwrap_or_else(|_| chrono::Utc::now());

    Ok(Message {
        id: row.try_get("id")?,
        date_time,
        group_id: row.try_get("group_id")?,
        message_id: row.try_get("message_id")?,
        from_user_id: row.try_get("from_user_id")?,
        reply_to_user_id: row.try_get("reply_to_user_id")?,
        reply_to_message_id: row.try_get("reply_to_message_id")?,
        content: row.try_get("content")?,
    })
}

fn row_to_user_with_group(row: &sqlx::sqlite::SqliteRow) -> std::result::Result<UserWithGroup, sqlx::Error> {
    use sqlx::Row;

    Ok(UserWithGroup {
        id: row.try_get("id")?,
        user_id: row.try_get("user_id")?,
        group_id: row.try_get("group_id")?,
    })
}

fn row_to_group_settings(row: &sqlx::sqlite::SqliteRow) -> std::result::Result<GroupSettings, sqlx::Error> {
    use sqlx::Row;

    Ok(GroupSettings {
        id: row.try_get("id")?,
        group_id: row.try_get("group_id")?,
        enable_search: row.try_get::<i32, _>("enable_search")? != 0,
        enable_ai: row.try_get::<i32, _>("enable_ai")? != 0,
        enable_ocr: row.try_get::<i32, _>("enable_ocr")? != 0,
        enable_asr: row.try_get::<i32, _>("enable_asr")? != 0,
    })
}

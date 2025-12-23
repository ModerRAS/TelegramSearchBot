//! 配置管理模块
//!
//! 从 Config.json 加载配置，类似 C# 版本的 Env.cs

use once_cell::sync::Lazy;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::fs;
use std::path::PathBuf;
use std::sync::RwLock;
use tracing::{info, warn};

/// 配置文件结构
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct Config {
    #[serde(rename = "BaseUrl")]
    pub base_url: String,

    #[serde(rename = "BotToken")]
    pub bot_token: String,

    #[serde(rename = "AdminId")]
    pub admin_id: i64,

    #[serde(rename = "EnableAutoOCR")]
    pub enable_auto_ocr: bool,

    #[serde(rename = "EnableAutoASR")]
    pub enable_auto_asr: bool,

    #[serde(rename = "IsLocalAPI")]
    pub is_local_api: bool,

    #[serde(rename = "SameServer")]
    pub same_server: bool,

    #[serde(rename = "TaskDelayTimeout")]
    pub task_delay_timeout: u64,

    #[serde(rename = "OllamaModelName")]
    pub ollama_model_name: String,

    #[serde(rename = "EnableVideoASR")]
    pub enable_video_asr: bool,

    #[serde(rename = "EnableOpenAI")]
    pub enable_openai: bool,

    #[serde(rename = "OpenAIModelName")]
    pub openai_model_name: String,

    #[serde(rename = "OLTPAuth")]
    pub oltp_auth: Option<String>,

    #[serde(rename = "OLTPAuthUrl")]
    pub oltp_auth_url: Option<String>,

    #[serde(rename = "OLTPName")]
    pub oltp_name: Option<String>,

    #[serde(rename = "BraveApiKey")]
    pub brave_api_key: Option<String>,

    #[serde(rename = "EnableAccounting")]
    pub enable_accounting: bool,

    #[serde(rename = "MaxToolCycles")]
    pub max_tool_cycles: u32,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            base_url: "https://api.telegram.org".to_string(),
            bot_token: String::new(),
            admin_id: 0,
            enable_auto_ocr: false,
            enable_auto_asr: false,
            is_local_api: false,
            same_server: false,
            task_delay_timeout: 1000,
            ollama_model_name: "qwen2.5:72b-instruct-q2_K".to_string(),
            enable_video_asr: false,
            enable_openai: false,
            openai_model_name: "gpt-4o".to_string(),
            oltp_auth: None,
            oltp_auth_url: None,
            oltp_name: None,
            brave_api_key: None,
            enable_accounting: false,
            max_tool_cycles: 25,
        }
    }
}

/// 全局环境配置
pub struct Env {
    pub config: Config,
    pub work_dir: PathBuf,
    pub bot_id: RwLock<i64>,
    pub scheduler_port: RwLock<u16>,
    pub configuration: RwLock<HashMap<String, String>>,
}

impl Env {
    /// 获取工作目录
    pub fn get_work_dir() -> PathBuf {
        if cfg!(windows) {
            dirs::data_local_dir()
                .unwrap_or_else(|| PathBuf::from("."))
                .join("TelegramSearchBot")
        } else {
            dirs::data_dir()
                .unwrap_or_else(|| PathBuf::from("."))
                .join("TelegramSearchBot")
        }
    }

    /// 加载配置
    pub fn load() -> Self {
        let work_dir = Self::get_work_dir();

        // 确保工作目录存在
        if !work_dir.exists() {
            fs::create_dir_all(&work_dir).expect("无法创建工作目录");
        }

        let config_path = work_dir.join("Config.json");
        let config = if config_path.exists() {
            match fs::read_to_string(&config_path) {
                Ok(content) => match serde_json::from_str(&content) {
                    Ok(config) => {
                        info!("配置加载成功: {:?}", config_path);
                        config
                    }
                    Err(e) => {
                        warn!("配置文件解析失败: {}, 使用默认配置", e);
                        Config::default()
                    }
                },
                Err(e) => {
                    warn!("配置文件读取失败: {}, 使用默认配置", e);
                    Config::default()
                }
            }
        } else {
            info!("配置文件不存在，创建默认配置");
            let default_config = Config::default();
            if let Ok(json) = serde_json::to_string_pretty(&default_config) {
                let _ = fs::write(&config_path, json);
            }
            default_config
        };

        Self {
            config,
            work_dir,
            bot_id: RwLock::new(0),
            scheduler_port: RwLock::new(6379),
            configuration: RwLock::new(HashMap::new()),
        }
    }

    /// 获取日志目录
    pub fn log_dir(&self) -> PathBuf {
        self.work_dir.join("logs")
    }

    /// 获取数据库路径
    pub fn database_path(&self) -> PathBuf {
        self.work_dir.join("Data.sqlite")
    }

    /// 获取索引目录
    pub fn index_dir(&self) -> PathBuf {
        self.work_dir.join("Index_Data")
    }

    /// 获取 FAISS 索引目录
    pub fn faiss_index_dir(&self) -> PathBuf {
        self.work_dir.join("faiss_indexes")
    }

    /// 设置 Bot ID
    pub fn set_bot_id(&self, id: i64) {
        if let Ok(mut bot_id) = self.bot_id.write() {
            *bot_id = id;
        }
    }

    /// 获取 Bot ID
    pub fn get_bot_id(&self) -> i64 {
        self.bot_id.read().map(|id| *id).unwrap_or(0)
    }

    /// 设置调度器端口
    pub fn set_scheduler_port(&self, port: u16) {
        if let Ok(mut scheduler_port) = self.scheduler_port.write() {
            *scheduler_port = port;
        }
    }

    /// 获取调度器端口
    pub fn get_scheduler_port(&self) -> u16 {
        self.scheduler_port.read().map(|port| *port).unwrap_or(6379)
    }

    /// 设置配置项
    pub fn set_configuration(&self, key: &str, value: &str) {
        if let Ok(mut config) = self.configuration.write() {
            config.insert(key.to_string(), value.to_string());
        }
    }

    /// 获取配置项
    pub fn get_configuration(&self, key: &str) -> Option<String> {
        self.configuration.read().ok()?.get(key).cloned()
    }
}

/// 全局配置实例
pub static ENV: Lazy<Env> = Lazy::new(Env::load);

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_default_config() {
        let config = Config::default();
        assert_eq!(config.base_url, "https://api.telegram.org");
        assert!(!config.enable_auto_ocr);
        assert_eq!(config.max_tool_cycles, 25);
    }

    #[test]
    fn test_work_dir() {
        let work_dir = Env::get_work_dir();
        assert!(work_dir.to_string_lossy().contains("TelegramSearchBot"));
    }
}

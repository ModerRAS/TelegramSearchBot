//! TelegramSearchBot Common - 配置和通用类型
//!
//! 这个模块包含所有项目共用的配置管理和类型定义

pub mod config;
pub mod error;
pub mod model;

pub use config::Env;
pub use error::{Error, Result};

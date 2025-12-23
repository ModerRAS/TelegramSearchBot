//! 服务模块
//!
//! 包含各种业务服务，类似 C# 版本的 Service 层

mod container;
mod search_service;

pub use container::ServiceContainer;
pub use search_service::SearchService;

//! 控制器模块
//!
//! 类似 C# 版本的 Controller 层，处理各种命令和消息

mod executor;
mod pipeline;
mod search;
mod storage;

pub use executor::ControllerExecutor;
pub use pipeline::PipelineContext;

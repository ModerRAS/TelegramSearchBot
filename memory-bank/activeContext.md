# Active Context - HTTP反向代理子进程开发

## 当前状态
- 已完成标准HTTP反向代理实现
- 主要功能：
  - 监听指定端口接收HTTP请求
  - 从Redis获取路由配置(端口→目标服务器)
  - 完整请求/响应转发
  - 支持请求头转发
  - 每个请求独立HttpClient实例

## 技术实现
1. 核心架构：
   - 基于Kestrel的多端口HTTP服务器
   - Redis动态配置路由(端口→目标服务器)
   - 每个端口独立监听进程
   - 使用特定前缀避免key冲突(TelegramSearchBot:HttpProxy:)

2. 关键特性：
   - 动态配置：通过Redis Set存储所有监听端口
   - 实时监听配置变更并动态调整服务
   - 请求转发：完整转发请求头、请求体和所有HTTP方法
   - 自动处理Content-Type和请求体流
   - 每个请求独立HttpClient实例
   - 支持系统默认代理配置(自动适配Windows/Linux系统设置)
   - 完善的错误处理和日志记录
   - 错误处理：完善的错误日志和状态码返回
   - 资源隔离：每个请求独立HttpClient实例

2. 关键设计：
   - 动态路由配置(通过Redis实时更新)
   - 请求头完整转发
   - 错误处理和日志记录
   - 10分钟超时自动退出

## 新增工具类
1. HttpProxyHelper.cs:
   - 提供Redis配置管理接口
   - 方法:
     - AddProxyConfig: 添加/更新代理配置
     - RemoveProxyConfig: 移除代理配置
   - 使用前缀TelegramSearchBot:HttpProxy:避免key冲突

## 后续工作
1. 功能扩展：
   - 支持请求体转发(POST/PUT等)
   - 添加健康检查端点
   - 实现负载均衡

2. 性能优化：
   - 连接池管理
   - 响应缓存
   - 请求限流

3. 部署集成：
   - 容器化配置
   - 监控指标暴露
   - 自动化测试

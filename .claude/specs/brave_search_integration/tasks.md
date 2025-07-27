# Brave Search集成实现任务

## 1. [ ] 更新配置系统
### 1.1. [ ] 在配置模型中添加Brave API密钥
- 修改`Env.cs`中的`Config`类以包含`BraveApiKey`属性
- 引用需求1.1：系统应从Config.json文件读取Brave Search API密钥

### 1.2. [ ] 在Env静态类中添加Brave API密钥
- 在`Env`静态类中添加`BraveApiKey`属性
- 在静态构造函数中添加从Config.json加载BraveApiKey的代码
- 引用需求1.1：系统应从Config.json文件读取Brave Search API密钥
- 引用需求1.2：系统应支持通过Env.cs配置的环境变量覆盖API密钥

## 2. [ ] 创建Brave Search数据模型
### 2.1. [ ] 创建BraveSearchResult模型
- 在`Model/Tools/`目录中创建`BraveSearchResult.cs`文件
- 定义与Brave Search API响应格式匹配的模型
- 包括类：`BraveSearchResult`, `BraveWebResults`, `BraveResultItem`, `BraveProfile`
- 引用需求5.1：系统应从Brave Search结果中提取标题、URL和描述

### 2.2. [ ] 创建IBraveSearchService接口
- 在`Interface/Tools/`目录中创建`IBraveSearchService.cs`文件
- 定义搜索操作的接口
- 引用需求3.2：系统应将DuckDuckGo服务实现替换为Brave Search实现

## 3. [ ] 实现Brave Search服务
### 3.1. [ ] 创建BraveSearchService类
- 在`Service/Tools/`目录中创建`BraveSearchService.cs`文件
- 实现`IBraveSearchService`接口
- 使用`IHttpClientFactory`创建HTTP客户端（支持代理）
- 引用需求2.1：系统应将搜索查询发送到Brave Search API端点

### 3.2. [ ] 实现搜索方法
- 创建带有适当参数的`SearchWeb`方法
- 在请求标头中添加Brave Search API密钥
- 根据Brave Search API规范格式化请求
- 引用需求2.1：系统应将搜索查询发送到Brave Search API端点

### 3.3. [ ] 实现响应解析
- 解析来自Brave Search API的JSON响应
- 转换为`BraveSearchResult`对象
- 优雅地处理缺失或格式错误的数据
- 引用需求2.2：系统应处理API响应并解析JSON结果

### 3.4. [ ] 添加错误处理
- 为瞬时故障实现重试逻辑
- 添加请求超时配置
- 处理速率限制响应（429）
- 处理身份验证错误（401）
- 引用需求4.1：系统应为瞬时API故障实现重试逻辑

## 4. [ ] 检查AI服务兼容性
### 4.1. [ ] 验证McpToolHelper兼容性
- 检查`ConvertToolResultToString`方法是否能正确处理`BraveSearchResult`
- 必要时更新结果格式化逻辑以适配Brave Search结果结构
- 确保结果格式化符合Telegram消息显示要求
- 引用需求2.3：系统应格式化搜索结果以供Telegram消息显示

### 4.2. [ ] 验证OllamaService兼容性
- 验证通过反射自动连接的Brave Search服务能被正确调用
- 检查结果处理逻辑是否与新的数据结构兼容
- 引用需求3.2：系统应将DuckDuckGo服务实现替换为Brave Search实现

### 4.3. [ ] 验证OpenAIService兼容性
- 验证通过反射自动连接的Brave Search服务能被正确调用
- 检查结果处理逻辑是否与新的数据结构兼容
- 引用需求3.2：系统应将DuckDuckGo服务实现替换为Brave Search实现

## 5. [ ] 添加配置验证
### 5.1. [ ] 在启动时验证API密钥
- 在应用程序启动中添加验证逻辑
- 为缺失或无效的API配置提供清晰的错误消息
- 引用需求1.3：系统应在启动时验证API密钥配置

## 6. [ ] 更新文档
### 6.1. [ ] 更新架构概述
- 修改`Docs/Architecture_Overview.md`以引用Brave Search而不是DuckDuckGo
- 引用需求3.1：系统应删除所有与DuckDuckGo相关的代码文件和引用

### 6.2. [ ] 更新代码库概述
- 修改`Docs/Existing_Codebase_Overview.md`以引用Brave Search
- 引用需求3.1：系统应删除所有与DuckDuckGo相关的代码文件和引用

## 7. [ ] 删除DuckDuckGo实现
### 7.1. [ ] 删除DuckDuckGo文件
- 删除`DuckDuckGoToolService.cs`
- 删除`IDuckDuckGoToolService.cs`
- 删除`DuckDuckGoSearchResult.cs`
- 引用需求3.1：系统应删除所有与DuckDuckGo相关的代码文件和引用

### 7.2. [ ] 删除DuckDuckGo测试文件
- 删除`DuckDuckGoToolServiceTest.cs`
- 引用需求3.1：系统应删除所有与DuckDuckGo相关的代码文件和引用

## 8. [ ] 创建单元测试
### 8.1. [ ] 创建BraveSearchService测试
- 在测试项目中创建`BraveSearchServiceTest.cs`
- 使用mock数据测试JSON响应解析流程
- 验证结果对象正确构建
- 测试错误处理逻辑（无效JSON、缺失字段等）
- 引用需求4.3：系统应记录API交互以用于调试目的
# Brave Search集成设计

## 概述
本文档概述了将现有的DuckDuckGo搜索功能替换为Brave Search API集成的设计。该实现将保持现有的架构模式，同时通过Brave的API提供更可靠的搜索服务。

## 架构
该设计遵循TelegramSearchBot项目中的现有服务模式：
1. 新的`BraveSearchService`实现`IBraveSearchService`接口
2. 与现有的MediatR管道集成以执行工具
3. 通过现有的`Env`配置系统进行配置
4. 用于解析Brave Search API响应的数据模型
5. 更新的AI服务集成（Ollama, OpenAI）以处理Brave Search结果

## 组件和接口

### 1. 服务层
**BraveSearchService.cs**
- 使用Brave Search API实现搜索功能
- 处理带有适当标头和身份验证的HTTP通信
- 将JSON响应解析为结构化数据模型
- 提供错误处理和重试逻辑

**IBraveSearchService.cs**
- 定义搜索操作契约的接口
- 与现有服务接口保持一致性

### 2. 数据模型
**BraveSearchResult.cs**
- 与Brave Search API响应格式匹配的数据结构
- 包含标题、URL、描述和元数据的结果项

### 3. 配置
- 通过`Config.json`进行API密钥配置，并支持环境变量覆盖
- 在应用程序启动时进行验证

### 4. AI服务集成
- 更新`McpToolHelper`以处理Brave Search结果格式化
- 修改`OllamaService`和`OpenAIService`以处理Brave Search结果

## 数据模型

### 请求模型
```
BraveSearchRequest {
  string Query     // 搜索查询文本
  int Page         // 结果页码（默认：1）
  int Count        // 要返回的结果数（默认：5）
  string Country   // 国家代码（可选，默认："us"）
  string SearchLang // 搜索语言（可选，默认："en"）
}
```

### 响应模型
```
BraveSearchResult {
  string Type      // 始终为"search"
  BraveWebResults Web  // Web搜索结果
}

BraveWebResults {
  string Type              // 始终为"search"
  List<BraveResultItem> Results  // 搜索结果列表
}

BraveResultItem {
  string Title         // 结果页面标题
  string Url           // 结果页面URL
  string Description   // 描述结果的片段
  bool IsSourceLocal   // 是否为本地来源
  bool IsSourceBoth    // 是否为本地和网络来源
  BraveProfile Profile // 来源信息（可选）
}

BraveProfile {
  string Name      // 来源名称
  string Url       // 来源URL
  string LongName  // 完整来源名称
  string Img       // 来源favicon URL
}
```

## API调用规范

### 请求端点
- URL: `https://api.search.brave.com/res/v1/web/search`
- 方法: GET

### 请求头
```
Accept: application/json
Accept-Encoding: gzip
X-Subscription-Token: [API密钥]
```

### 查询参数
- q: 搜索查询文本（必需）
- count: 返回结果数（可选，默认：5，最大：20）
- country: 国家代码（可选，默认："us"）
- search_lang: 搜索语言（可选，默认："en"）

## 错误处理
1. **配置错误**：在启动时验证API密钥并提供清晰的错误消息
2. **网络错误**：为瞬时故障实现指数退避重试逻辑
3. **API错误**：处理速率限制（429）、身份验证错误（401）和服务器错误（5xx）
4. **解析错误**：优雅地处理格式错误的API响应
5. **超时**：实现请求超时（默认：10秒）以防止挂起请求

## 测试策略
1. **单元测试**：使用模拟的HTTP响应测试BraveSearchService
2. **集成测试**：验证配置加载和API通信
3. **AI服务测试**：更新现有测试以处理Brave Search结果格式化
4. **错误处理测试**：验证各种错误条件下的行为
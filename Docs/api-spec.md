# TelegramSearchBot API 规范文档

## 概述

本文档定义了TelegramSearchBot的REST API接口规范，包括消息管理、搜索功能、AI服务等核心API。

## API 基础信息

- **基础URL**: `/api/v1`
- **认证方式**: Bearer Token (Telegram Bot Token)
- **内容类型**: `application/json`
- **字符编码**: `UTF-8`

## 通用响应格式

### 成功响应
```json
{
  "success": true,
  "data": {},
  "message": "操作成功",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 错误响应
```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "输入验证失败",
    "details": ["Message content cannot be empty"]
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 分页响应
```json
{
  "success": true,
  "data": {
    "items": [],
    "pagination": {
      "page": 1,
      "pageSize": 20,
      "totalCount": 100,
      "totalPages": 5
    }
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

## 消息管理 API

### 1. 创建消息

**请求**:
```http
POST /api/v1/messages
Authorization: Bearer {bot_token}
Content-Type: application/json
```

**请求体**:
```json
{
  "groupId": 123456789,
  "messageId": 987654321,
  "content": "Hello World",
  "fromUserId": 123456,
  "timestamp": "2024-01-01T00:00:00Z",
  "replyToMessageId": 0,
  "replyToUserId": 0
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "id": 987654321,
    "groupId": 123456789,
    "content": "Hello World",
    "fromUserId": 123456,
    "timestamp": "2024-01-01T00:00:00Z",
    "createdAt": "2024-01-01T00:00:00Z"
  },
  "message": "消息创建成功",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 2. 获取消息详情

**请求**:
```http
GET /api/v1/messages/{groupId}/{messageId}
Authorization: Bearer {bot_token}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "id": 987654321,
    "groupId": 123456789,
    "messageId": 987654321,
    "content": "Hello World",
    "fromUserId": 123456,
    "timestamp": "2024-01-01T00:00:00Z",
    "replyToMessageId": 0,
    "replyToUserId": 0,
    "extensions": [
      {
        "type": "image",
        "data": "{\"url\":\"https://example.com/image.jpg\"}"
      }
    ],
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-01T00:00:00Z"
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 3. 获取群组消息列表

**请求**:
```http
GET /api/v1/groups/{groupId}/messages?page=1&pageSize=20
Authorization: Bearer {bot_token}
```

**查询参数**:
| 参数 | 类型 | 必需 | 默认值 | 描述 |
|------|------|------|--------|------|
| page | int | 否 | 1 | 页码 |
| pageSize | int | 否 | 20 | 每页数量 |
| startDate | string | 否 | null | 开始日期 (ISO 8601) |
| endDate | string | 否 | null | 结束日期 (ISO 8601) |
| userId | long | 否 | null | 用户ID过滤 |

**响应**:
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": 987654321,
        "groupId": 123456789,
        "messageId": 987654321,
        "content": "Hello World",
        "fromUserId": 123456,
        "timestamp": "2024-01-01T00:00:00Z",
        "createdAt": "2024-01-01T00:00:00Z"
      }
    ],
    "pagination": {
      "page": 1,
      "pageSize": 20,
      "totalCount": 100,
      "totalPages": 5
    }
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 4. 更新消息

**请求**:
```http
PUT /api/v1/messages/{groupId}/{messageId}
Authorization: Bearer {bot_token}
Content-Type: application/json
```

**请求体**:
```json
{
  "content": "Updated message content"
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "id": 987654321,
    "groupId": 123456789,
    "content": "Updated message content",
    "fromUserId": 123456,
    "timestamp": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-01T00:00:00Z"
  },
  "message": "消息更新成功",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 5. 删除消息

**请求**:
```http
DELETE /api/v1/messages/{groupId}/{messageId}
Authorization: Bearer {bot_token}
```

**响应**:
```json
{
  "success": true,
  "data": null,
  "message": "消息删除成功",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

## 搜索 API

### 1. 全文搜索

**请求**:
```http
GET /api/v1/search/messages?query=hello&groupId=123456789&page=1&pageSize=20
Authorization: Bearer {bot_token}
```

**查询参数**:
| 参数 | 类型 | 必需 | 默认值 | 描述 |
|------|------|------|--------|------|
| query | string | 是 | - | 搜索关键词 |
| groupId | long | 否 | null | 群组ID过滤 |
| page | int | 否 | 1 | 页码 |
| pageSize | int | 否 | 20 | 每页数量 |
| startDate | string | 否 | null | 开始日期 |
| endDate | string | 否 | null | 结束日期 |
| userId | long | 否 | null | 用户ID过滤 |

**响应**:
```json
{
  "success": true,
  "data": {
    "results": [
      {
        "id": 987654321,
        "groupId": 123456789,
        "messageId": 987654321,
        "content": "Hello World",
        "fromUserId": 123456,
        "timestamp": "2024-01-01T00:00:00Z",
        "score": 0.95,
        "highlights": [
          {
            "field": "content",
            "fragment": "<em>Hello</em> World"
          }
        ]
      }
    ],
    "pagination": {
      "page": 1,
      "pageSize": 20,
      "totalCount": 50,
      "totalPages": 3
    },
    "query": "hello",
    "searchTime": 0.045
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 2. 语义搜索

**请求**:
```http
POST /api/v1/search/semantic
Authorization: Bearer {bot_token}
Content-Type: application/json
```

**请求体**:
```json
{
  "query": "What is the meaning of life?",
  "groupId": 123456789,
  "limit": 10,
  "threshold": 0.7
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "results": [
      {
        "id": 987654321,
        "groupId": 123456789,
        "messageId": 987654321,
        "content": "The meaning of life is 42",
        "fromUserId": 123456,
        "timestamp": "2024-01-01T00:00:00Z",
        "score": 0.85,
        "similarity": 0.85
      }
    ],
    "query": "What is the meaning of life?",
    "searchTime": 0.123
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 3. 搜索建议

**请求**:
```http
GET /api/v1/search/suggestions?query=hel&groupId=123456789&limit=5
Authorization: Bearer {bot_token}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "suggestions": [
      {
        "text": "hello",
        "frequency": 150,
        "score": 0.9
      },
      {
        "text": "help",
        "frequency": 89,
        "score": 0.8
      }
    ]
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

## AI 服务 API

### 1. OCR 识别

**请求**:
```http
POST /api/v1/ai/ocr
Authorization: Bearer {bot_token}
Content-Type: multipart/form-data
```

**表单数据**:
| 参数 | 类型 | 必需 | 描述 |
|------|------|------|------|
| image | file | 是 | 图片文件 |
| language | string | 否 | 识别语言 (默认: 'ch') |

**响应**:
```json
{
  "success": true,
  "data": {
    "text": "识别出的文本内容",
    "confidence": 0.95,
    "language": "ch",
    "processingTime": 1.234
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 2. ASR 语音识别

**请求**:
```http
POST /api/v1/ai/asr
Authorization: Bearer {bot_token}
Content-Type: multipart/form-data
```

**表单数据**:
| 参数 | 类型 | 必需 | 描述 |
|------|------|------|------|
| audio | file | 是 | 音频文件 |
| language | string | 否 | 识别语言 (默认: 'zh') |

**响应**:
```json
{
  "success": true,
  "data": {
    "text": "识别出的语音内容",
    "confidence": 0.88,
    "language": "zh",
    "duration": 5.6,
    "processingTime": 2.345
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 3. LLM 对话

**请求**:
```http
POST /api/v1/ai/llm/chat
Authorization: Bearer {bot_token}
Content-Type: application/json
```

**请求体**:
```json
{
  "message": "你好，请介绍一下自己",
  "model": "gpt-3.5-turbo",
  "temperature": 0.7,
  "maxTokens": 1000,
  "context": [
    {
      "role": "system",
      "content": "你是一个智能助手"
    }
  ]
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "response": "你好！我是一个智能助手，可以帮助你回答问题、提供信息...",
    "model": "gpt-3.5-turbo",
    "usage": {
      "promptTokens": 25,
      "completionTokens": 45,
      "totalTokens": 70
    },
    "processingTime": 1.567
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 4. 图像分析

**请求**:
```http
POST /api/v1/ai/vision
Authorization: Bearer {bot_token}
Content-Type: multipart/form-data
```

**表单数据**:
| 参数 | 类型 | 必需 | 描述 |
|------|------|------|------|
| image | file | 是 | 图片文件 |
| prompt | string | 否 | 分析提示 (默认: '请描述这张图片') |

**响应**:
```json
{
  "success": true,
  "data": {
    "description": "这是一张美丽的风景照片，包含山脉和湖泊...",
    "objects": [
      {
        "name": "mountain",
        "confidence": 0.92,
        "boundingBox": { "x": 100, "y": 50, "width": 200, "height": 150 }
      }
    ],
    "processingTime": 2.123
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

## 群组管理 API

### 1. 获取群组列表

**请求**:
```http
GET /api/v1/groups?page=1&pageSize=20
Authorization: Bearer {bot_token}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": 123456789,
        "title": "测试群组",
        "type": "group",
        "memberCount": 150,
        "messageCount": 5000,
        "lastActivity": "2024-01-01T00:00:00Z",
        "createdAt": "2023-01-01T00:00:00Z"
      }
    ],
    "pagination": {
      "page": 1,
      "pageSize": 20,
      "totalCount": 10,
      "totalPages": 1
    }
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 2. 获取群组统计

**请求**:
```http
GET /api/v1/groups/{groupId}/statistics
Authorization: Bearer {bot_token}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "groupId": 123456789,
    "totalMessages": 5000,
    "totalUsers": 150,
    "activeUsers": 45,
    "messagesToday": 25,
    "messagesThisWeek": 180,
    "messagesThisMonth": 750,
    "topUsers": [
      {
        "userId": 123456,
        "messageCount": 250,
        "percentage": 5.0
      }
    ],
    "topKeywords": [
      {
        "keyword": "hello",
        "count": 150,
        "percentage": 3.0
      }
    ]
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

## 用户管理 API

### 1. 获取用户信息

**请求**:
```http
GET /api/v1/users/{userId}
Authorization: Bearer {bot_token}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "id": 123456,
    "username": "john_doe",
    "firstName": "John",
    "lastName": "Doe",
    "isBot": false,
    "messageCount": 150,
    "groupCount": 5,
    "lastActivity": "2024-01-01T00:00:00Z",
    "createdAt": "2023-01-01T00:00:00Z"
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 2. 获取用户消息

**请求**:
```http
GET /api/v1/users/{userId}/messages?groupId=123456789&page=1&pageSize=20
Authorization: Bearer {bot_token}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": 987654321,
        "groupId": 123456789,
        "messageId": 987654321,
        "content": "Hello World",
        "timestamp": "2024-01-01T00:00:00Z",
        "createdAt": "2024-01-01T00:00:00Z"
      }
    ],
    "pagination": {
      "page": 1,
      "pageSize": 20,
      "totalCount": 150,
      "totalPages": 8
    }
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

## 系统管理 API

### 1. 获取系统状态

**请求**:
```http
GET /api/v1/system/status
Authorization: Bearer {bot_token}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "status": "healthy",
    "version": "1.0.0",
    "uptime": "2d 5h 30m",
    "database": {
      "status": "connected",
      "connectionCount": 5
    },
    "search": {
      "status": "healthy",
      "indexCount": 1000,
      "indexSize": "50MB"
    },
    "cache": {
      "status": "connected",
      "memoryUsage": "25MB",
      "hitRate": 0.85
    },
    "ai": {
      "status": "available",
      "services": {
        "ocr": "available",
        "asr": "available",
        "llm": "available"
      }
    }
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 2. 获取系统配置

**请求**:
```http
GET /api/v1/system/config
Authorization: Bearer {bot_token}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "bot": {
      "token": "******",
      "adminId": 123456789,
      "enableCommands": true
    },
    "ai": {
      "enableOCR": true,
      "enableASR": true,
      "enableLLM": true,
      "ocrLanguage": "ch",
      "asrLanguage": "zh",
      "llmModel": "gpt-3.5-turbo"
    },
    "search": {
      "enableFullText": true,
      "enableSemantic": true,
      "maxResults": 100
    },
    "storage": {
      "databaseType": "sqlite",
      "cacheEnabled": true,
      "backupEnabled": true
    }
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 3. 更新系统配置

**请求**:
```http
PUT /api/v1/system/config
Authorization: Bearer {bot_token}
Content-Type: application/json
```

**请求体**:
```json
{
  "ai": {
    "enableOCR": true,
    "enableASR": false,
    "enableLLM": true,
    "ocrLanguage": "en",
    "llmModel": "gpt-4"
  },
  "search": {
    "maxResults": 50
  }
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "updatedFields": ["ai.enableASR", "ai.ocrLanguage", "ai.llmModel", "search.maxResults"],
    "message": "配置更新成功"
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

## 错误代码

| 错误代码 | HTTP状态码 | 描述 |
|----------|------------|------|
| `UNAUTHORIZED` | 401 | 未授权访问 |
| `FORBIDDEN` | 403 | 禁止访问 |
| `NOT_FOUND` | 404 | 资源不存在 |
| `VALIDATION_ERROR` | 400 | 输入验证失败 |
| `CONFLICT` | 409 | 资源冲突 |
| `INTERNAL_ERROR` | 500 | 服务器内部错误 |
| `SERVICE_UNAVAILABLE` | 503 | 服务不可用 |
| `RATE_LIMIT_EXCEEDED` | 429 | 请求频率超限 |

## 数据类型

### MessageDto
```typescript
interface MessageDto {
  id: number;
  groupId: number;
  messageId: number;
  content: string;
  fromUserId: number;
  timestamp: string;
  replyToMessageId?: number;
  replyToUserId?: number;
  extensions?: MessageExtensionDto[];
  createdAt: string;
  updatedAt?: string;
}
```

### MessageExtensionDto
```typescript
interface MessageExtensionDto {
  type: string;
  data: string;
  createdAt: string;
}
```

### PaginationDto
```typescript
interface PaginationDto {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
```

### SearchResultDto
```typescript
interface SearchResultDto {
  id: number;
  groupId: number;
  messageId: number;
  content: string;
  fromUserId: number;
  timestamp: string;
  score: number;
  highlights?: HighlightDto[];
}
```

### HighlightDto
```typescript
interface HighlightDto {
  field: string;
  fragment: string;
}
```

## 认证与授权

### Bot Token 认证
所有API请求都需要在Header中包含有效的Bot Token：
```
Authorization: Bearer {bot_token}
```

### 权限控制
- **普通用户**: 只能访问公开数据和自己的数据
- **群组管理员**: 可以管理群组内的消息
- **系统管理员**: 可以访问所有管理API

## 速率限制

- **普通API**: 100次/分钟
- **搜索API**: 60次/分钟
- **AI服务API**: 30次/分钟
- **管理API**: 10次/分钟

## Webhook 事件

### 消息事件
```json
{
  "eventType": "message.created",
  "data": {
    "id": 987654321,
    "groupId": 123456789,
    "content": "Hello World",
    "fromUserId": 123456,
    "timestamp": "2024-01-01T00:00:00Z"
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

### 系统事件
```json
{
  "eventType": "system.error",
  "data": {
    "error": "Database connection failed",
    "severity": "high",
    "component": "database"
  },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

## 版本控制

### API 版本
- 当前版本: `v1`
- 版本格式: `v{major}.{minor}.{patch}`
- 向后兼容: 只在major版本更新时破坏兼容性

### 版本策略
- **v1**: 当前稳定版本
- **v2**: 开发中版本
- 旧版本支持: 至少维护6个月

## 示例代码

### C# 示例
```csharp
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

public class TelegramSearchBotClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _botToken;
    
    public TelegramSearchBotClient(string botToken, string baseUrl = "https://api.example.com")
    {
        _botToken = botToken;
        _baseUrl = baseUrl;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {botToken}");
    }
    
    public async Task<MessageDto> CreateMessageAsync(CreateMessageRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/v1/messages", request);
        
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<MessageDto>>();
        return result!.Data;
    }
    
    public async Task<PagedList<MessageDto>> SearchMessagesAsync(
        string query, long? groupId = null, int page = 1, int pageSize = 20)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["query"] = query,
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        
        if (groupId.HasValue)
        {
            queryParams["groupId"] = groupId.Value.ToString();
        }
        
        var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var response = await _httpClient.GetAsync(
            $"{_baseUrl}/api/v1/search/messages?{queryString}");
        
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<PagedList<MessageDto>>>();
        return result!.Data;
    }
}
```

### Python 示例
```python
import requests
import json
from typing import Dict, Any, Optional

class TelegramSearchBotClient:
    def __init__(self, bot_token: str, base_url: str = "https://api.example.com"):
        self.bot_token = bot_token
        self.base_url = base_url
        self.headers = {
            "Authorization": f"Bearer {bot_token}",
            "Content-Type": "application/json"
        }
    
    def create_message(self, message_data: Dict[str, Any]) -> Dict[str, Any]:
        response = requests.post(
            f"{self.base_url}/api/v1/messages",
            headers=self.headers,
            json=message_data
        )
        response.raise_for_status()
        return response.json()
    
    def search_messages(self, query: str, group_id: Optional[int] = None, 
                       page: int = 1, page_size: int = 20) -> Dict[str, Any]:
        params = {
            "query": query,
            "page": page,
            "pageSize": page_size
        }
        
        if group_id:
            params["groupId"] = group_id
        
        response = requests.get(
            f"{self.base_url}/api/v1/search/messages",
            headers=self.headers,
            params=params
        )
        response.raise_for_status()
        return response.json()
```

## 测试指南

### 单元测试
- 使用xUnit进行单元测试
- 模拟外部依赖
- 测试所有边界条件

### 集成测试
- 使用TestServer进行集成测试
- 测试完整的API流程
- 验证数据库操作

### 性能测试
- 使用BenchmarkDotNet进行性能测试
- 测试关键API的响应时间
- 验证并发处理能力

## 部署指南

### 开发环境
```bash
# 启动开发服务器
dotnet run --environment Development

# 运行测试
dotnet test

# 生成API文档
dotnet swagger
```

### 生产环境
```bash
# 构建发布版本
dotnet publish -c Release -o ./publish

# 使用Docker部署
docker build -t telegram-search-bot .
docker run -d -p 8080:80 telegram-search-bot
```

## 监控与日志

### 日志格式
```json
{
  "timestamp": "2024-01-01T00:00:00Z",
  "level": "Information",
  "message": "API request processed",
  "properties": {
    "method": "GET",
    "path": "/api/v1/messages",
    "statusCode": 200,
    "duration": 45,
    "userId": "123456"
  }
}
```

### 监控指标
- 请求计数和响应时间
- 错误率和异常计数
- 数据库查询性能
- 缓存命中率
- AI服务调用延迟

## 总结

本API规范文档定义了TelegramSearchBot的完整REST API接口，包括消息管理、搜索功能、AI服务等核心功能。通过统一的接口设计、标准化的响应格式和完善的错误处理，确保了API的可用性和可维护性。

**关键特性**：
- RESTful API设计
- 统一的响应格式
- 完善的错误处理
- 分页和过滤支持
- 认证和授权
- 速率限制
- 完整的文档和示例

**最佳实践**：
- 使用HTTPS进行安全通信
- 实现输入验证和清理
- 使用异步操作提高性能
- 实现缓存策略
- 完善的日志记录
- 定期安全审计
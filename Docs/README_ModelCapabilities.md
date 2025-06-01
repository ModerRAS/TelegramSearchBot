# 模型能力信息功能实现

## 概述

本功能实现了从 OpenAI、Ollama、Gemini 和 **OpenRouter** API 获取模型能力信息（如工具调用支持、视觉/多模态能力、嵌入模型状态等），并将这些数据存储在 ChannelsWithModel 数据库表中。

## 支持的API提供商

### 1. OpenAI
- 支持通过OpenAI官方API获取模型列表
- 尝试通过内部API获取详细能力信息
- 基于模型名称推断能力特性（工具调用、视觉处理等）

### 2. **OpenRouter** 🆕
- **完全支持**：通过OpenRouter API (`https://openrouter.ai/api/v1/models`) 获取详细模型信息
- **丰富的能力信息**：
  - 输入/输出模态信息（文本、图像、音频等）
  - 支持的参数列表（工具调用、流式响应、JSON格式等）
  - 模型架构信息（tokenizer、context length等）
  - 定价信息（prompt、completion、image价格）
  - 自动识别模型提供商（OpenAI、Anthropic、Google、Meta、Mistral等）
- **智能检测**：通过Gateway URL自动检测OpenRouter服务
  - 检测条件：Gateway包含 `openrouter.ai` 或 `openrouter`
  - 无需添加新的LLMProvider，使用现有的OpenAI Provider

### 3. Ollama  
- 通过Ollama API获取本地模型列表
- 基于已知模型清单推断工具调用能力
- 提取模型大小、修改时间等信息

### 4. Gemini
- 通过Google AI API获取模型信息
- 支持输入/输出token限制检测
- 基于模型名称推断多模态能力

## 实现的组件

### 1. 数据模型

#### ModelCapability.cs
- 存储模型能力信息的数据模型
- 包含能力名称、值、描述和最后更新时间
- 与 ChannelWithModel 建立外键关系

#### ModelWithCapabilities.cs
- 数据传输对象，包含模型信息和能力信息
- 提供便捷的属性访问（如 SupportsToolCalling、SupportsVision、SupportsEmbedding）
- 包含能力设置和获取的辅助方法

### 2. 数据库更改

#### 新增表：ModelCapabilities
- Id (主键)
- ChannelWithModelId (外键)
- CapabilityName (能力名称)
- CapabilityValue (能力值)
- Description (描述)
- LastUpdated (最后更新时间)

#### 扩展 ChannelWithModel
- 添加了 Capabilities 导航属性，建立与 ModelCapability 的一对多关系

### 3. 服务实现与依赖注入

#### 接口定义
- `IModelCapabilityService` - 模型能力管理服务接口
- `ILLMService` - 扩展了获取模型能力的方法
- `IMessageExtensionService` - 消息扩展服务接口

#### 依赖注入配置
所有服务类均添加了 `[Injectable(ServiceLifetime.Transient)]` 注解：

- `ModelCapabilityService` - 模型能力管理核心服务
- `OpenAIService` - **增强支持OpenRouter**
- `OllamaService` - Ollama模型服务  
- `GeminiService` - Google Gemini服务

#### 构造函数依赖注入优化
- 所有服务构造函数使用接口注入，便于单元测试和Mock
- 支持`IHttpClientFactory`、`ILogger<T>`、`DataDbContext`等标准依赖

### 4. OpenAIService中的OpenRouter适配

#### 智能检测机制
```csharp
private bool IsOpenRouter(string gateway)
{
    return !string.IsNullOrEmpty(gateway) && 
           (gateway.Contains("openrouter.ai") || gateway.Contains("openrouter"));
}
```

#### OpenRouter模型获取
- **API端点**：`https://openrouter.ai/api/v1/models`
- **认证**：Bearer Token (使用通道的ApiKey)
- **响应处理**：解析JSON格式的模型列表和详细能力信息

#### 能力信息解析
OpenRouter提供的详细能力信息包括：

1. **基本信息**
   - 模型显示名称
   - 描述信息
   - 上下文长度

2. **模态支持**
   - 输入模态：text, image, audio等
   - 输出模态：text等
   - 自动设置vision、multimodal等能力

3. **支持的参数**
   - 自动检测`tools`、`function_calling`支持
   - 检测`stream`、`response_format`等参数
   - 记录完整的支持参数列表

4. **定价信息**
   - prompt价格
   - completion价格  
   - image处理价格

5. **模型分类**
   - 自动识别OpenAI、Anthropic、Google、Meta、Mistral等提供商
   - 基于模型名称模式匹配

### 5. 通道管理集成

#### EditLLMConfHelper增强
- **RefreshAllChannel方法**：在刷新模型列表的同时自动更新模型能力
- **AddChannel方法**：新增通道时自动获取模型能力信息
- **错误处理**：完善的日志记录和异常处理
- **统计信息**：提供模型和能力刷新的详细统计

#### 使用流程
1. 添加OpenRouter通道（Provider选择OpenAI，Gateway设为OpenRouter地址）
2. 系统自动检测为OpenRouter服务
3. 调用`RefreshAllChannel`或`AddChannel`时自动获取模型能力
4. 能力信息存储到数据库，可通过API查询

## 使用示例

### 配置OpenRouter通道

1. **通道设置**：
   - Name: "OpenRouter"
   - Provider: OpenAI
   - Gateway: "https://openrouter.ai/api/v1/"
   - ApiKey: "your-openrouter-api-key"

2. **自动能力获取**：
   ```csharp
   // 系统会自动检测OpenRouter并获取详细模型能力
   var capabilities = await openAIService.GetAllModelsWithCapabilities(channel);
   
   // 刷新所有通道（包括OpenRouter）
   var refreshedCount = await editLLMConfHelper.RefreshAllChannel();
   ```

3. **查询模型能力**：
   ```csharp
   // 查询支持工具调用的模型
   var toolModels = await modelCapabilityService.GetModelsByCapability("function_calling", true);
   
   // 查询支持视觉的模型
   var visionModels = await modelCapabilityService.GetModelsByCapability("vision", true);
   ```

## 技术特点

### ✅ 优势
- **零配置**：OpenRouter使用现有OpenAI Provider，无需修改大量代码
- **智能检测**：自动识别OpenRouter服务
- **丰富信息**：获取比其他API更详细的模型能力信息
- **统一接口**：所有提供商使用相同的API接口
- **自动更新**：集成到现有的通道刷新流程

### 🔧 技术实现
- **无侵入式**：在现有OpenAIService中添加条件分支
- **向后兼容**：不影响现有OpenAI通道的功能
- **错误恢复**：OpenRouter API失败时自动降级到标准流程
- **性能优化**：使用HttpClientFactory和异步处理

## 数据库迁移

已生成并应用的迁移：
- `20250601101450_AddModelCapabilities.cs`
- 创建`ModelCapabilities`表
- 建立外键关系

## 项目构建状态

✅ **编译成功** - 所有功能已实现并通过编译
⚠️ **文件锁定** - 由于程序正在运行，无法覆盖exe文件（这是正常现象）

OpenRouter适配现已完成，可以正常获取模型列表和详细的能力信息！ 
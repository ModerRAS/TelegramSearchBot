# 基于对话段的向量搜索功能

## 功能概述

本次更新实现了基于对话段的向量搜索功能，相比之前的单条消息向量化，这种方式能够更好地保留上下文信息，提供更准确的语义搜索结果。

## 核心改进

### 1. 对话段分析
- **智能分段**：根据时间间隔、消息数量、内容长度和话题变化自动分段
- **话题检测**：简单的关键词分析和话题转换信号检测
- **上下文保留**：每个段包含完整的对话上下文

### 2. 向量化策略
- **段级向量化**：对整个对话段进行向量化，而不是单条消息
- **丰富的元数据**：包含时间、参与者、话题关键词等信息
- **语义增强**：结合内容摘要和关键词提升搜索准确性

## 技术架构

### 数据模型

#### ConversationSegment（对话段）
```csharp
public class ConversationSegment
{
    public long Id { get; set; }                    // 主键
    public long GroupId { get; set; }               // 群组ID
    public DateTime StartTime { get; set; }         // 开始时间
    public DateTime EndTime { get; set; }           // 结束时间
    public long FirstMessageId { get; set; }        // 第一条消息ID
    public long LastMessageId { get; set; }         // 最后一条消息ID
    public int MessageCount { get; set; }           // 消息数量
    public int ParticipantCount { get; set; }       // 参与者数量
    public string ContentSummary { get; set; }      // 内容摘要
    public string TopicKeywords { get; set; }       // 话题关键词
    public string FullContent { get; set; }         // 完整内容
    public string VectorId { get; set; }            // 向量ID
    public bool IsVectorized { get; set; }          // 是否已向量化
}
```

#### ConversationSegmentMessage（对话段消息关联）
```csharp
public class ConversationSegmentMessage
{
    public long ConversationSegmentId { get; set; } // 对话段ID
    public long MessageDataId { get; set; }         // 消息ID
    public int SequenceOrder { get; set; }          // 序列顺序
}
```

### 核心服务

#### 1. ConversationSegmentationService（对话分段服务）
**功能**：
- 智能分段算法
- 话题检测和转换识别
- 关键词提取
- 内容摘要生成

**分段策略**：
```csharp
// 分段参数
private const int MaxMessagesPerSegment = 10;      // 每段最大消息数
private const int MinMessagesPerSegment = 3;       // 每段最小消息数
private const int MaxTimeGapMinutes = 15;          // 最大时间间隔（分钟）
private const int MaxSegmentLengthChars = 2000;    // 每段最大字符数
private const double TopicSimilarityThreshold = 0.3; // 话题相似度阈值
```

**分段触发条件**：
1. 消息数量达到上限（10条）
2. 时间间隔超过15分钟
3. 字符数超过2000个
4. 话题发生明显变化
5. 检测到话题转换信号（如"另外"、"顺便"、"@"等）

#### 2. ConversationVectorService（对话向量服务）
**功能**：
- 对话段向量生成和存储
- 基于对话段的语义搜索
- 向量数据库管理

**向量内容构建**：
```csharp
private string BuildVectorContent(ConversationSegment segment)
{
    var contentBuilder = new StringBuilder();
    
    // 时间信息
    contentBuilder.AppendLine($"时间: {segment.StartTime:yyyy-MM-dd HH:mm} - {segment.EndTime:yyyy-MM-dd HH:mm}");
    
    // 参与者信息
    contentBuilder.AppendLine($"参与者数量: {segment.ParticipantCount}");
    
    // 话题关键词
    if (!string.IsNullOrEmpty(segment.TopicKeywords))
        contentBuilder.AppendLine($"话题关键词: {segment.TopicKeywords}");
    
    // 内容摘要
    if (!string.IsNullOrEmpty(segment.ContentSummary))
        contentBuilder.AppendLine($"内容摘要: {segment.ContentSummary}");
    
    // 完整对话内容
    contentBuilder.AppendLine("对话内容:");
    contentBuilder.AppendLine(segment.FullContent);
    
    return contentBuilder.ToString();
}
```

#### 3. ConversationProcessingService（对话处理后台服务）
**功能**：
- 定期处理新消息
- 自动分段和向量化
- 增量更新处理

## 使用方式

### 搜索命令
```
向量搜索 如何学习编程
```

### 搜索流程
1. **查询向量生成**：将搜索词转换为向量
2. **向量相似度搜索**：在对话段向量库中搜索
3. **结果聚合**：根据对话段获取相关消息
4. **结果排序**：按相似度和时间排序

### 搜索结果
搜索结果会显示：
- 相关对话段中的所有消息
- 按时间顺序排列
- 保留完整的对话上下文
- 支持分页浏览

## 优势对比

### 传统单消息向量化 vs 对话段向量化

| 特性 | 单消息向量化 | 对话段向量化 |
|------|-------------|-------------|
| **上下文保留** | ❌ 缺失 | ✅ 完整保留 |
| **语义理解** | ❌ 片段化 | ✅ 连贯理解 |
| **搜索准确性** | ⚠️ 一般 | ✅ 显著提升 |
| **存储效率** | ❌ 冗余 | ✅ 优化 |
| **话题连贯性** | ❌ 断裂 | ✅ 保持 |

### 实际效果示例

**查询**：`向量搜索 如何学习编程`

**单消息向量化可能返回**：
- "Python很好学"
- "我在学Java"
- "编程难吗？"

**对话段向量化会返回**：
```
用户A: 我想学编程，有什么建议吗？
用户B: 建议从Python开始，语法简单易懂
用户A: Python主要用来做什么？
用户B: 可以做数据分析、网站开发、自动化脚本等
用户C: 我也推荐Python，有很多在线教程
用户A: 谢谢！有推荐的学习网站吗？
```

## 配置和部署

### 1. 数据库迁移
```bash
dotnet ef database update
```

### 2. 后台服务启动
`ConversationProcessingService` 会自动启动，定期处理新消息。

### 3. 手动触发分段
```csharp
// 为特定群组创建对话段
await segmentationService.CreateSegmentsForGroupAsync(groupId);

// 向量化群组的所有对话段
await conversationVectorService.VectorizeGroupSegments(groupId);
```

## 性能优化

### 1. 增量处理
- 只处理新消息，避免重复计算
- 智能检测需要重新分段的群组

### 2. 批量向量化
- 批量处理多个对话段
- 异步向量生成和存储

### 3. 缓存策略
- 向量结果缓存
- 分段结果缓存

## 监控和维护

### 日志记录
- 分段过程详细日志
- 向量化成功/失败统计
- 搜索性能监控

### 健康检查
```csharp
// 检查向量服务健康状态
bool isHealthy = await conversationVectorService.IsHealthyAsync();
```

### 数据统计
- 对话段数量统计
- 向量化进度监控
- 搜索质量评估

## 未来扩展

### 1. 高级话题分析
- 使用LLM进行更精确的话题检测
- 情感分析和意图识别
- 多语言支持

### 2. 个性化搜索
- 用户偏好学习
- 搜索历史分析
- 个性化排序

### 3. 实时处理
- 流式处理新消息
- 实时分段和向量化
- 即时搜索更新

## 总结

基于对话段的向量搜索功能显著提升了语义搜索的准确性和用户体验。通过智能分段、话题检测和上下文保留，用户能够获得更相关、更连贯的搜索结果。这种方式特别适合群聊环境，能够有效处理多话题并行的复杂对话场景。 
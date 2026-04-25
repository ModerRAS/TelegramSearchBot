# FAISS向量搜索系统

## 概述

基于 **SQLite + FAISS.NET** 的向量搜索系统，提供：

- ✅ **零额外服务依赖** - 不需要外部向量数据库
- ✅ **高性能搜索** - FAISS是Facebook开发的高效向量相似性搜索库
- ✅ **简化部署** - 所有数据存储在SQLite，索引文件存储在本地
- ✅ **可靠性提升** - 避免网络连接问题和服务管理复杂性

## 技术架构

### 存储策略

1. **SQLite数据库** - 存储向量元数据和映射关系
   - VectorIndex表：记录向量在FAISS索引中的位置
   - FaissIndexFile表：记录索引文件信息和状态
   - 复用现有的ConversationSegment表

2. **FAISS索引文件** - 存储实际的向量数据
   - 每个群组一个索引文件：`{GroupId}_ConversationSegment.faiss`
   - 存储位置：`{WorkDir}/faiss_indexes/`
   - 使用IVFFlat索引算法，适合中等规模数据

### 数据模型

#### VectorIndex（向量索引元数据）
```csharp
public class VectorIndex
{
    public long Id { get; set; }                    // 主键
    public long GroupId { get; set; }               // 群组ID
    public string VectorType { get; set; }          // 向量类型（ConversationSegment/Message）
    public long EntityId { get; set; }              // 相关实体ID
    public long FaissIndex { get; set; }            // 在FAISS索引中的位置
    public string ContentSummary { get; set; }      // 内容摘要
    public DateTime CreatedAt { get; set; }         // 创建时间
    public DateTime UpdatedAt { get; set; }         // 更新时间
}
```

#### FaissIndexFile（索引文件信息）
```csharp
public class FaissIndexFile
{
    public long Id { get; set; }                    // 主键
    public long GroupId { get; set; }               // 群组ID
    public string IndexType { get; set; }           // 索引类型
    public string FilePath { get; set; }            // 文件路径
    public int Dimension { get; set; }              // 向量维度（1024）
    public long VectorCount { get; set; }           // 向量数量
    public long FileSize { get; set; }              // 文件大小
    public bool IsValid { get; set; }               // 是否有效
}
```

### 核心服务

#### FaissVectorService
负责FAISS向量的生成、存储和搜索：

**主要功能**：
- 对话段向量化
- 相似性搜索
- 索引管理
- 批量处理

**搜索流程**：
```csharp
1. 生成查询向量 → GenerateVectorAsync(query)
2. 加载FAISS索引 → GetOrCreateIndexAsync(groupId)
3. 执行相似性搜索 → index.Search(queryVector, topK)
4. 根据索引位置获取实体 → 查询VectorIndex表
5. 获取对话段消息 → 查询ConversationSegmentMessage表
6. 返回排序结果
```

**索引策略**：
- 小规模数据（<100向量）：使用暴力搜索
- 中等规模数据：使用IVFFlat索引
- 自动训练：每100个向量训练一次

## 使用方式

### 基本搜索命令

```bash
# 向量搜索
向量搜索 如何学习编程

# 传统倒排索引搜索（不变）
搜索 Python编程
```

### 管理命令

管理员可以使用以下命令管理FAISS系统：

#### `/faiss状态` - 查看系统状态
```
FAISS向量数据库状态报告

🔍 健康状态: ✅ 正常

📊 索引文件统计:
   • 对话段索引: 15 个
   • 单消息索引: 0 个
   • 总向量数量: 1,234

💾 存储使用: 45.2 MB
📁 索引目录: 15 个文件
```

#### `/faiss健康检查` - 快速健康检查
```
✅ FAISS向量服务运行正常
📁 索引目录正常
```

#### `/faiss统计` - 详细统计信息
```
📊 FAISS向量数据库详细统计

🗣️ 对话段统计:
   • 总对话段: 2,456
   • 已向量化: 2,234
   • 待处理: 222

🔢 向量索引统计:
   • ConversationSegment: 2,234

📈 活跃群组 Top 10:
   • 群组 -123456789: 156 个对话段
   • 群组 -987654321: 134 个对话段
```

#### `/faiss重建` - 重建索引
```
🔄 开始重建FAISS向量索引...
✅ FAISS向量索引重建完成

📊 成功重建群组: 15/15
🗣️ 处理对话段: 2,234 个
```

#### `/faiss清理` - 清理无效数据
```
🧹 开始清理FAISS数据

✅ 清理完成
   • 删除无效索引记录: 5
   • 删除孤立向量记录: 12
   • 删除孤立文件: 3
```

## 性能优化

### 内存管理
- **延迟加载**：索引文件仅在需要时加载
- **LRU缓存**：最近使用的索引保持在内存中
- **资源释放**：定期清理不活跃的索引

### 搜索优化
- **批量处理**：支持批量向量化以提高效率
- **并行搜索**：私聊搜索时并行查询多个群组
- **分页优化**：智能分页避免大量数据传输

### 存储优化
- **增量更新**：仅处理新的对话段
- **文件压缩**：FAISS内置压缩算法
- **定期清理**：自动清理孤立和无效数据

## 故障排除

### 常见问题

1. **索引文件损坏**
   ```bash
   # 解决方案：重建索引
   /faiss重建
   ```

2. **内存不足**
   ```bash
   # 解决方案：清理无效数据
   /faiss清理
   ```

3. **搜索性能慢**
   ```bash
   # 检查索引状态
   /faiss统计
   
   # 重建索引以优化性能
   /faiss重建
   ```

### 日志监控

关注以下日志模式：
- `FAISS向量服务初始化`
- `对话段向量搜索完成`
- `FAISS索引训练完成`
- `向量化失败` - 需要关注的错误

## 技术细节

### FAISS索引算法选择

- **IVFFlat**: 适合中等规模（1K-1M向量）
- **暴力搜索**: 小规模数据的后备方案
- **内积距离**: 适合文本向量的语义相似性

### 向量化策略

1. **对话段内容构建**：
   ```
   时间: 2024-01-01 10:00 - 10:15
   参与者数量: 3
   话题关键词: Python, 编程, 学习
   内容摘要: 讨论Python编程学习方法
   对话内容: [完整对话文本]
   ```

2. **向量生成**：使用LLM服务生成1024维向量

3. **索引存储**：FAISS索引文件 + SQLite元数据

本方案提供可靠和简单的向量搜索体验。 
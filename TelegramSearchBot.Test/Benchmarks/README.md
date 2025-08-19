# TelegramSearchBot 性能测试套件

## 概述

本性能测试套件为 TelegramSearchBot 项目提供全面的性能基准测试，使用 BenchmarkDotNet 框架对核心功能进行性能分析和优化指导。

## 🎯 测试目标

- **MessageRepository**: 测试数据库操作的 CRUD 性能
- **MessageProcessingPipeline**: 测试消息处理管道的吞吐量
- **Lucene 搜索**: 测试全文搜索的响应时间和准确性
- **FAISS 向量搜索**: 测试语义搜索的性能表现

## 🏗️ 架构设计

### 核心组件

```
TelegramSearchBot.Test/
├── Benchmarks/
│   ├── Domain/Message/
│   │   ├── MessageRepositoryBenchmarks.cs
│   │   └── MessageProcessingBenchmarks.cs
│   ├── Search/
│   │   └── SearchPerformanceBenchmarks.cs
│   ├── Vector/
│   │   └── VectorSearchBenchmarks.cs
│   ├── BenchmarkProgram.cs
│   └── performance-config.json
└── run_performance_tests.sh
```

### 测试层次

1. **单元级性能测试**: 测试单个方法的性能
2. **集成级性能测试**: 测试组件间的交互性能
3. **系统级性能测试**: 测试端到端的业务流程性能

## 📊 测试套件详解

### 1. MessageRepositoryBenchmarks

**测试场景**:
- 小数据集查询 (100条消息)
- 中等数据集查询 (1,000条消息)
- 大数据集查询 (10,000条消息)
- 关键词搜索性能
- 批量插入/更新/删除操作

**关键指标**:
- 查询响应时间
- 内存分配量
- 垃圾回收频率
- 并发处理能力

### 2. MessageProcessingBenchmarks

**测试场景**:
- 单条消息处理
- 长消息处理 (500+ 词)
- 批量消息处理 (10-1000条)
- 不同内容类型 (中文/英文/特殊字符)
- 并发处理性能

**关键指标**:
- 消息处理吞吐量
- 内存使用效率
- 处理延迟
- 错误率

### 3. SearchPerformanceBenchmarks

**测试场景**:
- 简单关键词搜索
- 语法搜索 (短语/字段指定/排除词)
- 中文/英文搜索
- 分页搜索性能
- 索引构建性能

**关键指标**:
- 搜索响应时间
- 索引大小
- 查询准确性
- 索引构建时间

### 4. VectorSearchBenchmarks

**测试场景**:
- 向量生成性能
- 相似性搜索
- TopK 搜索
- 中英文向量搜索
- 索引构建性能

**关键指标**:
- 向量生成时间
- 相似性计算速度
- 内存使用量
- 搜索准确性

## 🚀 使用方法

### 命令行运行

```bash
# 运行 MessageRepository 性能测试
dotnet run --project TelegramSearchBot.Test -- repository

# 运行所有性能测试
dotnet run --project TelegramSearchBot.Test -- all

# 使用脚本运行 (推荐)
./run_performance_tests.sh repository
```

### 可用命令

| 命令 | 描述 |
|------|------|
| `repository` | MessageRepository 性能测试 |
| `processing` | MessageProcessingPipeline 性能测试 |
| `search` | Lucene 搜索性能测试 |
| `vector` | FAISS 向量搜索性能测试 |
| `all` | 运行所有性能测试 |
| `quick` | 快速测试 (小数据集) |

### 脚本选项

```bash
# 使用 Release 配置运行
./run_performance_tests.sh --release all

# 指定输出目录
./run_performance_tests.sh -o /custom/output/path repository

# 详细输出
./run_performance_tests.sh --verbose search

# 使用自定义配置
./run_performance_tests.sh -c custom-config.json vector
```

## ⚙️ 配置说明

### 性能基线配置

`performance-config.json` 定义了性能基线和目标：

```json
{
  "performanceBaselines": {
    "messageRepository": {
      "querySmallDataset": {
        "mean": "< 1ms",
        "allocated": "< 1KB"
      }
    }
  },
  "performanceTargets": {
    "responseTime": {
      "critical": "< 100ms",
      "acceptable": "< 1s"
    }
  }
}
```

### 测试参数配置

- **数据集大小**: 小(100)、中(1,000)、大(10,000)
- **迭代次数**: 3-10 次预热，5-10 次测量
- **随机种子**: 42 (确保结果可重现)
- **向量维度**: 1024 (FAISS 默认)

## 📈 结果分析

### BenchmarkDotNet 输出

性能测试生成以下文件：

- `results.html`: 交互式 HTML 报告
- `results.csv`: CSV 格式的详细数据
- `*-report.csv`: 统计摘要报告

### 关键指标解释

| 指标 | 说明 | 重要性 |
|------|------|--------|
| **Mean** | 平均执行时间 | ⭐⭐⭐⭐⭐ |
| **StdDev** | 标准差 (稳定性) | ⭐⭐⭐⭐ |
| **Allocated** | 内存分配量 | ⭐⭐⭐⭐⭐ |
| **Gen0/1/2** | 垃圾回收代数 | ⭐⭐⭐ |
| **Ops/sec** | 每秒操作数 | ⭐⭐⭐⭐ |

### 性能评级标准

- **🟢 优秀**: 性能超过基线 50%
- **🟡 良好**: 性能超过基线 20%
- **🟠 合格**: 性能达到基线标准
- **🔴 需优化**: 性能低于基线 20%

## 🔧 环境要求

### 系统要求

- **操作系统**: Windows/Linux/macOS
- **.NET 版本**: 9.0+
- **内存**: 最少 4GB，推荐 8GB+
- **存储**: 1GB 可用空间
- **CPU**: 4+ 核心处理器

### 依赖包

```xml
<PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
```

## 🧪 测试最佳实践

### 运行环境准备

1. **关闭不必要的服务**: 减少系统干扰
2. **使用电源高性能模式**: 确保CPU频率稳定
3. **重启应用程序**: 清理缓存和内存碎片
4. **多次运行取平均值**: 减少偶然误差

### 结果解读建议

1. **关注趋势而非绝对值**: 相对性能更重要
2. **结合业务场景**: 不同场景有不同要求
3. **考虑硬件差异**: 结果需要横向对比
4. **定期重新测试**: 跟踪性能变化

### 性能优化建议

#### 数据库优化
- 使用适当的索引
- 优化查询语句
- 考虑读写分离

#### 搜索优化
- 合理配置分词器
- 优化索引结构
- 使用缓存机制

#### 向量搜索优化
- 选择合适的向量维度
- 量化压缩减少内存
- 批量处理提高效率

## 🐛 常见问题

### Q: 测试结果波动很大

**A**: 
- 确保系统负载稳定
- 增加测试迭代次数
- 关闭后台应用程序
- 使用电源高性能模式

### Q: 内存使用过高

**A**:
- 检查内存泄漏
- 优化数据结构
- 使用对象池
- 调整垃圾回收策略

### Q: 向量搜索很慢

**A**:
- 检查向量维度设置
- 使用量化压缩
- 优化相似性计算
- 考虑GPU加速

### Q: 测试运行失败

**A**:
- 检查依赖项是否完整
- 确认测试数据存在
- 查看详细错误日志
- 验证环境配置

## 📝 扩展指南

### 添加新的性能测试

1. **创建测试类**: 继承现有的基准测试模式
2. **定义测试方法**: 使用 `[Benchmark]` 特性
3. **配置测试参数**: 设置迭代次数和数据大小
4. **实现测试逻辑**: 使用真实或模拟数据
5. **验证测试结果**: 确保测试的准确性

### 自定义性能指标

```csharp
[Benchmark]
[MemoryDiagnoser]
public void CustomBenchmark()
{
    // 自定义测试逻辑
}
```

### 集成到 CI/CD

```yaml
# GitHub Actions 示例
- name: Run Performance Tests
  run: ./run_performance_tests.sh quick
- name: Upload Performance Results
  uses: actions/upload-artifact@v2
  with:
    name: performance-results
    path: BenchmarkResults/
```

## 📊 性能监控

### 持续监控建议

1. **建立性能基线**: 记录正常状态下的性能指标
2. **设置告警阈值**: 定义性能下降的触发条件
3. **定期回归测试**: 确保新代码不影响性能
4. **趋势分析**: 跟踪性能变化趋势

### 性能报告模板

```
## 性能测试报告 - [日期]

### 测试环境
- 硬件配置: [CPU/内存/存储]
- 软件版本: [.NET版本/依赖版本]
- 测试数据: [数据集大小和特征]

### 关键发现
- 📈 性能改进: [具体改进点]
- 📉 性能回归: [需要关注的问题]
- ⚠️  风险点: [潜在的性能风险]

### 建议行动
1. [高优先级行动项]
2. [中优先级行动项]
3. [低优先级行动项]
```

## 🤝 贡献指南

欢迎贡献性能测试用例和优化建议！

### 贡献流程

1. **Fork 项目** 创建功能分支
2. **添加测试** 确保测试覆盖率
3. **运行测试** 验证性能改进
4. **提交 PR** 提供详细说明

### 代码规范

- 使用现有的测试模式
- 提供详细的注释说明
- 包含性能基线数据
- 遵循命名约定

## 📄 许可证

本性能测试套件遵循主项目的许可证条款。

---

**注意**: 性能测试结果受多种因素影响，建议结合实际业务场景进行解读和优化。
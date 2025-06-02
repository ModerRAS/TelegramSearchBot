# FAISS向量索引单元测试

本目录包含了针对TelegramSearchBot项目中FAISS向量索引功能的完整单元测试套件。

## 📁 测试文件结构

```
Service/Vector/
├── FaissVectorServiceTests.cs          # FAISS向量服务核心功能测试
├── VectorIndexTests.cs                 # 向量索引数据模型测试
├── VectorSearchIntegrationTests.cs     # 向量搜索集成测试
├── VectorPerformanceTests.cs           # 向量操作性能测试
└── README.md                           # 测试说明文档
```

## 🧪 测试覆盖范围

### 1. FaissVectorServiceTests.cs
**测试核心FAISS向量服务功能**

- ✅ **向量生成测试**：验证文本向量化功能
- ✅ **对话段向量化**：测试对话段的向量存储
- ✅ **重复向量检测**：确保已存在向量不被重复处理
- ✅ **向量搜索**：验证基于向量的相似性搜索
- ✅ **批量向量化**：测试群组对话段的批量处理
- ✅ **健康检查**：验证服务状态检测功能
- ✅ **空结果处理**：测试无匹配结果的场景
- ✅ **索引文件管理**：验证FAISS索引文件的创建和管理
- ✅ **分页支持**：测试搜索结果的分页功能

### 2. VectorIndexTests.cs
**测试向量索引相关数据模型**

- ✅ **VectorIndex模型**：测试向量索引实体的属性和行为
- ✅ **FaissIndexFile模型**：测试FAISS索引文件实体
- ✅ **数据库约束**：验证唯一性约束和索引
- ✅ **CRUD操作**：测试基本的增删改查操作
- ✅ **查询功能**：验证复杂查询条件
- ✅ **数据完整性**：确保数据的一致性

### 3. VectorSearchIntegrationTests.cs
**测试完整的向量搜索流程**

- ✅ **端到端搜索流程**：从向量化到搜索的完整链路
- ✅ **多对话段搜索**：在大量对话段中的搜索功能
- ✅ **分页搜索**：验证搜索结果的分页处理
- ✅ **无结果场景**：测试搜索无匹配时的处理
- ✅ **索引重建**：验证索引重建后的搜索功能
- ✅ **向量唯一性**：确保不同内容生成不同向量
- ✅ **数据一致性**：验证数据库和FAISS索引的一致性

### 4. VectorPerformanceTests.cs
**测试性能和稳定性**

- ✅ **大批量向量化性能**：测试100个对话段的向量化时间
- ✅ **搜索性能**：验证大索引下的搜索响应时间
- ✅ **持续性能**：测试多次搜索的性能一致性
- ✅ **并发处理**：验证并发向量化的正确性
- ✅ **内存使用**：监控大批量操作的内存消耗

## 🔧 技术实现

### 测试框架和工具
- **xUnit**: 主要测试框架
- **Moq**: 用于模拟依赖项
- **EntityFramework InMemory**: 内存数据库用于测试
- **ITestOutputHelper**: 用于性能测试的输出

### 模拟和测试数据
- **LLM服务模拟**: 使用确定性的随机向量生成
- **数据库模拟**: 使用EF Core InMemory数据库
- **文件系统隔离**: 每个测试使用独立的临时目录
- **测试数据生成**: 创建真实的对话段和消息数据

### 测试隔离
- **数据库隔离**: 每个测试使用独立的数据库实例
- **文件系统隔离**: 使用临时目录避免文件冲突
- **环境变量**: 动态设置工作目录路径
- **资源清理**: 测试完成后自动清理临时资源

## 🚀 运行测试

### 运行所有向量测试
```bash
# 在项目根目录执行
dotnet test TelegramSearchBot.Test --filter "Vector"
```

### 运行特定测试类
```bash
# 运行FAISS服务测试
dotnet test --filter "FaissVectorServiceTests"

# 运行性能测试
dotnet test --filter "VectorPerformanceTests"

# 运行集成测试
dotnet test --filter "VectorSearchIntegrationTests"
```

### 使用测试脚本
```powershell
# 运行完整的测试套件并生成报告
./TelegramSearchBot.Test/RunVectorTests.ps1
```

## 📊 性能基准

### 预期性能指标
- **向量化性能**: 100个对话段 < 30秒
- **搜索性能**: 单次搜索 < 2秒
- **内存使用**: 大批量处理后内存增长 < 50MB
- **并发处理**: 支持并发向量化操作

### 性能测试方法
- 使用`Stopwatch`测量操作耗时
- 通过`GC.GetTotalMemory()`监控内存使用
- 多次测试取平均值确保结果可靠性
- 设置合理的性能阈值进行断言

## 🛠️ 扩展测试

### 添加新测试
1. 在相应的测试类中添加新的测试方法
2. 使用`[Fact]`或`[Theory]`特性标记
3. 遵循AAA模式 (Arrange-Act-Assert)
4. 确保测试的独立性和可重复性

### 测试命名规范
```csharp
[Fact]
public async Task MethodName_Scenario_ExpectedResult()
{
    // 测试实现
}
```

### Mock设置
```csharp
// 设置LLM服务模拟
_mockLLMService.Setup(x => x.GenerateEmbeddingsAsync(It.IsAny<string>()))
              .ReturnsAsync(expectedVector);

// 设置数据库上下文
_mockServiceScope.Setup(x => x.ServiceProvider.GetRequiredService<DataDbContext>())
                 .Returns(_dbContext);
```

## 🔍 故障排除

### 常见问题

1. **测试超时**
   - 检查性能阈值设置是否合理
   - 确认测试环境的性能状况

2. **文件访问错误**
   - 确保测试有写入临时目录的权限
   - 检查是否有文件锁定问题

3. **内存泄漏**
   - 确保测试结束后正确释放资源
   - 检查`Dispose()`方法的实现

4. **数据库冲突**
   - 使用唯一的数据库名称
   - 确保测试之间的数据隔离

### 调试技巧
- 使用`ITestOutputHelper`输出调试信息
- 在测试方法中设置断点
- 检查临时文件的内容
- 使用性能分析工具监控资源使用

## 📝 贡献指南

### 提交新测试
1. 确保测试通过且稳定
2. 添加适当的注释和文档
3. 遵循现有的代码风格
4. 更新相关文档

### 代码审查要点
- 测试覆盖率和质量
- 性能影响评估
- 错误处理的完整性
- 测试的可维护性

---

**维护者**: TelegramSearchBot团队  
**最后更新**: 2024年12月  
**测试框架版本**: xUnit 2.9.3, .NET 8.0 
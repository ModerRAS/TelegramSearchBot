# Active Context - Qdrant Integration & Web Scraping

## Recent Changes
1. 完成Qdrant基础设施集成：
    - 在Env.cs中添加Qdrant路径配置
    - 实现QdrantDownloader服务处理二进制下载
    - 实现QdrantProcessManager管理进程生命周期
    - 更新GeneralBootstrap.cs中的服务注册
2. 完成VectorGenerationService核心功能：
    - 实现批量向量生成方法(GenerateVectorsAsync)
    - 实现相似性搜索功能(SearchSimilarAsync)
    - 添加健康检查机制(IsHealthyAsync)
3. Web Scraping功能：
    - 添加WebScraperController处理URL消息
    - 实现IWebScraperService基础HTML抓取功能
    - 与MessageExtensionService集成存储结果
4. GeneralLLMService改进：
    - 将IsSaturatedAsync改为GetAvailableCapacityAsync
    - 修改ScanAndProcessAltImageFiles使用批量任务添加
    - 修复异步方法参数问题

## Implementation Details
- Qdrant集成：
    - 使用HttpClient下载Qdrant二进制文件
    - 实现SHA256校验验证下载完整性
    - ProcessManager处理进程启动/停止/监控
    - 通过环境变量配置Qdrant路径和端口
- VectorGenerationService：
    - 支持多模型向量生成(默认openai)
    - 使用Task.WhenAll实现批量处理
    - 相似性搜索支持limit参数控制结果数量
    - 健康检查包含Qdrant连接和模型可用性验证
- Web Scraping：
    - WebScraperController检测消息中的URL实体
    - WebScraperService使用HttpClient获取并清理HTML内容
    - 结果存储在MessageExtensions中(最大1000字符)
- GeneralLLMService改进：
    - ScanAndProcessAltImageFiles使用GetAvailableCapacityAsync批量添加任务
    - 动态调整任务发送频率
    - 提取ProcessImageFileAsync方法解决异步参数问题

## Next Steps
- Qdrant功能优化：
    - 向量生成性能测试和优化
    - 相似性搜索效率优化
    - 添加向量索引管理功能
- Web Scraping增强：
    - 改进HTML内容提取
    - 添加URL验证和清理
    - 考虑添加常用URL缓存
- 性能监控：
    - 监控Qdrant内存使用
    - 跟踪Web Scraping性能
    - 建立向量生成性能基准

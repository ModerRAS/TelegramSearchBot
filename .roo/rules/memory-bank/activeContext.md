# Active Context - View Layer Refactoring

## 视图层重构计划

### 重构步骤
1. **ViewModel结构改造** (当前步骤)
   - 创建不依赖Telegram类型的ViewModel基础结构
   - 设计Builder模式接口
   - 确保所有消息类型都能通过ViewModel表达

2. **渲染层实现**
   - 完善IViewRenderer接口
   - 实现TelegramViewRenderer
   - 处理各种消息类型的渲染逻辑

3. **Service层改造**
   - 修改各Service返回ViewModel
   - 移除Service层对Telegram类型的直接依赖
   - 添加必要的转换逻辑

4. **Controller层调整**
   - 更新Controller处理流程
   - 协调Service和View层的交互
   - 确保原有功能不受影响

### ViewModel设计要点
- 完全独立于Telegram.Bot.Types
- 支持文本、媒体、命令等各种消息类型
- 使用Fluent Builder模式构建复杂消息
- 包含必要的元数据(chatId, replyTo等)

### 当前任务
- 修改TelegramViewModel.cs结构
- 确保Builder模式完整
- 添加必要的单元测试

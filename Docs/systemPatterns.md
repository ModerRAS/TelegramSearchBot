# 系统架构模式

## 核心架构
- 基于Telegram.Bot的异步消息处理架构
- 模块化控制器设计

## 设计模式
- 单一职责原则：
  - 应用于BiliMessageController重构
  - ProcessOpusInfoAsync专注Bilibili动态获取
  - HandleOpusInfoAsync专注Telegram消息处理
- 策略模式：
  - 通过OpusProcessingResult封装不同处理结果

## 关键实现路径
- Bilibili动态处理流程：
  1. ProcessOpusInfoAsync获取并处理动态信息
  2. 返回OpusProcessingResult封装结果
  3. HandleOpusInfoAsync根据结果生成Telegram消息

## 组件关系
- ProcessOpusInfoAsync与HandleOpusInfoAsync协作：
  - 前者为后者提供预处理结果
  - 后者依赖前者但不了解其实现细节
- OpusProcessingResult作为数据桥梁

## 数据流
- Bilibili动态数据流：
  1. 原始动态数据 → ProcessOpusInfoAsync
  2. 处理结果 → OpusProcessingResult
  3. 格式化结果 → HandleOpusInfoAsync → Telegram消息

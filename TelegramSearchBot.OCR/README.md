# TelegramSearchBot.OCR

独立的OCR文字识别服务，基于PaddleOCR实现。

## 功能特性

- 基于PaddleOCR的中文文字识别
- 通过Redis队列与主服务通信
- 支持Docker容器化部署
- 异步处理，支持并发控制
- 自动重连和错误恢复

## 技术栈

- .NET 7.0
- PaddleOCR
- OpenCV
- Redis
- Docker

## 快速开始

### 本地运行

1. 确保已安装Redis并运行在localhost:6379
2. 构建项目：
   ```bash
   dotnet build
   ```
3. 运行服务：
   ```bash
   dotnet run
   ```

### Docker运行

1. 使用docker-compose启动：
   ```bash
   docker-compose up -d
   ```

2. 单独构建和运行：
   ```bash
   docker build -t telegram-search-bot-ocr .
   docker run -d --name ocr-service -e REDIS_HOST=your-redis-host telegram-search-bot-ocr
   ```

## 配置

服务通过环境变量进行配置：

- `REDIS_HOST`: Redis主机地址，默认localhost
- `REDIS_PORT`: Redis端口，默认6379

## 通信协议

服务通过Redis队列与主服务通信：

1. 主服务将OCR任务推送到`OCRTasks`队列
2. OCR服务从队列获取任务，处理图片
3. 处理结果存储在`OCRResult-{taskId}`键中
4. 主服务通过等待该键获取结果

## 部署建议

- 建议将OCR服务部署在具有足够CPU资源的服务器上
- 可以部署多个OCR服务实例以提高处理能力
- 使用Redis集群确保高可用性
- 监控OCR处理时间和队列长度

## 监控

服务会输出详细的日志信息，包括：
- OCR模型初始化状态
- 任务处理开始和完成
- 错误和异常信息
- 性能指标

## 故障排除

1. **OCR模型加载失败**：检查PaddleOCR依赖是否正确安装
2. **Redis连接失败**：检查Redis服务状态和连接配置
3. **处理超时**：检查图片大小和服务器性能
4. **内存不足**：OCR处理需要较多内存，建议至少2GB内存
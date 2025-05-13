# TelegramSearchBot MediatR中介者模式控制器重构方案

## 1. 目标与背景

随着TelegramSearchBot项目逐步完成Orleans Actor模型的重构，系统的可扩展性和模块化能力大幅提升。为进一步提升业务逻辑的解耦性、可测试性和开发协作效率，建议将现有的Controller层（如AI、Bilibili、Download、Manage、Search等）统一重构为基于MediatR中介者模式的架构。

MediatR是一款轻量级的.NET中介者库，能够实现命令、查询、事件等消息的分发与处理，极大地降低各层之间的直接依赖，提升业务流程的灵活性和可维护性。

## 2. 架构设计原则

- **单一职责**：每个Handler只处理一种业务消息，逻辑清晰，便于维护。
- **解耦协作**：Controller不再直接依赖Service/Grain，而是通过MediatR分发消息，Handler负责具体业务处理。
- **可测试性**：业务逻辑集中在Handler，便于单元测试和Mock。
- **与Orleans协同**：MediatR Handler可作为Orleans Grain的调用入口，也可作为Grain内部的业务分发机制。
- **灵活扩展**：支持命令、查询、通知等多种消息模式，便于后续功能扩展。

## 3. MediatR中介者模式简介

MediatR通过`IMediator`接口实现消息的发布与分发，支持以下核心模式：
- **Request/Response（命令/查询）**：如`IRequest<TResponse>`，用于同步业务处理。
- **Notification（通知/事件）**：如`INotification`，用于广播型事件处理。
- **Pipeline Behaviors**：支持AOP式的请求管道（如日志、权限、事务等）。

## 4. 控制器重构方案

### 4.1 分层结构
- **API入口/消息入口**：如Bot消息、Web API、Orleans Stream等，统一将外部输入封装为MediatR Request/Notification。
- **MediatR Handler层**：每个业务场景对应一个或多个Handler，负责具体业务处理、调用Service/Grain、返回结果。
- **Service/Grain层**：保持原有业务实现，Handler通过依赖注入调用。

### 4.2 典型流程
1. **消息到达**（如Telegram Update、Orleans Stream消息、HTTP请求等）。
2. **入口适配器**将消息封装为MediatR Request/Notification对象。
3. **IMediator.Send/Publish**分发到对应Handler。
4. **Handler处理**业务逻辑，必要时调用Orleans Grain、Service、存储等。
5. **Handler返回结果**，由入口适配器/上层决定如何响应（如回复用户、推送下游、存储等）。

### 4.3 与Orleans集成
- Handler可直接注入`IGrainFactory`，通过MediatR消息驱动Grain调用。
- Orleans Stream消息消费后，统一转发为MediatR Notification，便于业务解耦和扩展。
- 支持Grain内部通过MediatR实现更细粒度的业务分发和事件响应。

## 5. 迁移步骤与建议

1. **梳理现有Controller职责**，将每个业务入口（如OCR、ASR、B站解析、搜索等）拆分为独立的Request/Notification类型。
2. **为每个业务场景实现对应的Handler**，将原Controller逻辑迁移到Handler中。
3. **入口适配器重构**：如Bot消息、Orleans流、Web API等，统一通过IMediator分发消息。
4. **依赖注入配置**：在`GeneralBootstrap`等启动类中注册MediatR及所有Handler。
5. **逐步替换原Controller调用路径**，通过功能开关或灰度方式平滑迁移。
6. **补充单元测试**，确保Handler的独立可测性。

## 6. 典型代码示例

### 6.1 定义Request/Notification
```csharp
// 以OCR为例
public class OcrRequest : IRequest<OcrResult>
{
    public long ChatId { get; set; }
    public int MessageId { get; set; }
    public byte[] ImageData { get; set; }
}

public class OcrResult
{
    public string Text { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
}
```

### 6.2 实现Handler
```csharp
public class OcrRequestHandler : IRequestHandler<OcrRequest, OcrResult>
{
    private readonly PaddleOCR _ocr;
    public OcrRequestHandler(PaddleOCR ocr)
    {
        _ocr = ocr;
    }
    public async Task<OcrResult> Handle(OcrRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _ocr.ExecuteAsync(new List<string> { Convert.ToBase64String(request.ImageData) });
            return new OcrResult { Text = string.Join("\n", result.Results.SelectMany(r => r.Select(x => x.Text))), Success = true };
        }
        catch (Exception ex)
        {
            return new OcrResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
```

### 6.3 入口适配器示例
```csharp
// 在Bot消息处理、Orleans流消费等入口：
var ocrRequest = new OcrRequest { ChatId = chatId, MessageId = msgId, ImageData = imageBytes };
var ocrResult = await _mediator.Send(ocrRequest);
if (ocrResult.Success)
{
    // 回复用户ocrResult.Text
}
else
{
    // 回复错误信息
}
```

## 7. 兼容性与测试建议
- **与Orleans无缝集成**：MediatR Handler可直接调用Grain，Orleans流可转为Notification。
- **单元测试友好**：Handler可独立Mock依赖，便于覆盖各种业务分支。
- **支持灰度迁移**：可通过功能开关逐步切换Controller到MediatR Handler。
- **与现有业务兼容**：可分阶段迁移，逐步替换原有Controller。

## 8. 总结

通过MediatR中介者模式重构Controller层，TelegramSearchBot将实现更彻底的解耦、可测试性和灵活性。结合Orleans的Actor模型和流机制，MediatR为业务流程编排和事件驱动提供了强大支撑。建议优先在AI、B站、搜索等核心模块试点，逐步推广到全项目，最终实现高内聚、低耦合、易维护的现代化Bot架构。 
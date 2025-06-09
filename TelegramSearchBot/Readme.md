# Telegram Search Bot
用于搜索Telegram用户发送信息的机器人

## 发送消息的设计思路
要考虑rate limit， 所以最好有一个队列来处理这个问题， 以避免429之后消息发不出去的问题。

## DTO
### SearchOptions
使用原理：
用于向下一页传递搜索数据
传递数据：
1. 搜索原字符串（Search）
2. 单页大小（Take）
3. 开始位置（Skip）
4. 搜索的GroupId， 用于拼接字符串，从群聊搜索可以搜索当前群的，通过私聊搜索是搜索当前用户所在的所有群

## 搜索思路
分为两种状态， 私聊bot和在群里搜索。

私聊bot通过检查chatId就可以检查出来， 这个ChatId是正数， 然后进入私聊搜索状态
群里搜索ChatId是负数，然后进入群聊搜索状态

### 私聊搜索状态
1. 这时先检查发送消息是否含有`搜索 `
2. 然后获取用户的UserId
3. 从数据库中搜索出这个UserId对应的GroupId
4. 生成一个搜索按键阵列？

### Telegram API 限制
Rate Limit 是如果是Group那么一分钟内只能20次， 全局共享一秒钟30次的发送速度。

### Chat Id
私聊是ChatId大于0， 群聊是小于0

## OCR坐标功能使用说明

### 概述
OCR识别功能已升级，现在统一使用带坐标的API，既可以获取识别的文本内容，也可以获取每个文字的精确坐标位置。

### 功能特点
- **统一API**: 后端统一使用带坐标的OCR处理
- **双重存储**: 既存储纯文本结果(`OCR_Result`)，也存储完整坐标信息(`OCR_Coordinates`)
- **PipelineContext集成**: 完整的OCR结果会放入`PipelineContext.PipelineCache["OCR_FullResult"]`中供其他处理器使用

### 使用方法

#### 1. 程序化调用
```csharp
// 方法1: 获取纯文本（向后兼容）
string text = await paddleOCRService.ExecuteAsync(imageStream);

// 方法2: 获取完整结果（包含坐标）
PaddleOCRResult fullResult = await paddleOCRService.ExecuteWithCoordinatesAsync(imageStream);

// 从完整结果中提取信息
foreach (var resultGroup in fullResult.Results) {
    foreach (var result in resultGroup) {
        string text = result.Text;           // 识别的文字
        double confidence = result.Confidence;  // 置信度
        var coordinates = result.TextRegion;    // 四角坐标 [x1,y1], [x2,y2], [x3,y3], [x4,y4]
    }
}
```

#### 2. 在其他控制器中获取OCR结果
```csharp
public async Task ExecuteAsync(PipelineContext p) {
    // 从PipelineContext获取完整的OCR结果
    if (p.PipelineCache.ContainsKey("OCR_FullResult")) {
        var ocrResult = (PaddleOCRResult)p.PipelineCache["OCR_FullResult"];
        
        // 处理坐标信息...
        foreach (var resultGroup in ocrResult.Results) {
            foreach (var result in resultGroup) {
                // 可以根据坐标做进一步处理，比如区域分析、布局识别等
                ProcessTextWithCoordinates(result.Text, result.TextRegion);
            }
        }
    }
}
```

#### 3. 从数据库获取坐标信息
```csharp
// 获取存储的坐标信息
var extensions = await messageExtensionService.GetByMessageDataIdAsync(messageDataId);
var coordinatesExtension = extensions.FirstOrDefault(x => x.Name == "OCR_Coordinates");
if (coordinatesExtension != null) {
    var ocrResult = JsonConvert.DeserializeObject<PaddleOCRResult>(coordinatesExtension.Value);
    // 使用坐标信息...
}
```

### 数据结构

#### PaddleOCRResult
```csharp
public class PaddleOCRResult {
    public string Message { get; set; }          // 状态消息
    public List<List<Result>> Results { get; set; }  // 识别结果列表
    public string Status { get; set; }           // 状态码 ("0"表示成功)
}
```

#### Result
```csharp
public class Result {
    public double Confidence { get; set; }       // 置信度 (0.0-1.0)
    public string Text { get; set; }             // 识别的文字
    public List<List<int>> TextRegion { get; set; }  // 四角坐标点 [[x1,y1],[x2,y2],[x3,y3],[x4,y4]]
}
```

### 坐标系说明
- 坐标原点(0,0)位于图片左上角
- X轴向右为正，Y轴向下为正
- TextRegion包含四个点，按顺序表示文字区域的四个角
- 坐标单位为像素

### 向后兼容性
- 原有的`ExecuteAsync`方法仍然可用，会自动调用带坐标的API然后提取文本
- 现有的`OCR_Result`扩展数据格式保持不变
- 新增的`OCR_Coordinates`扩展数据包含完整的坐标信息

### 性能说明
- 后端统一处理，避免重复OCR识别
- 完整结果会同时存储文本和坐标，满足不同使用场景的需求
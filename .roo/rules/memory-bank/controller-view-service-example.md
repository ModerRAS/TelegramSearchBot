# Controller-View-Service 核心模式

```csharp
// Controller: 协调流程
public class ExampleController : IOnUpdate
{
    private readonly ExampleService _service;
    private readonly ExampleView _view;

    public ExampleController(ExampleService service, ExampleView view)
    {
        _service = service;
        _view = view;
    }

    public async Task ExecuteAsync(PipelineContext context)
    {
        var message = context.Update.Message;
        var chatId = message.Chat.Id;
        var text = message.Text;
        
        var result = await _service.Process(new RequestData 
        {
            Text = text,
            ChatId = chatId
        });
        
        // 使用多个方法设置View数据
        await _view
            .WithChatId(result.ChatId)
            .WithText(result.Message)
            .AddButton("操作", "callback_data")
            .Render();
    }
}

// Service: 业务逻辑
public class ExampleService : IService
{
    public async Task<ResultData> Process(RequestData request)
    {
        return new ResultData 
        {
            Message = $"处理结果: {request.Text}",
            ChatId = request.ChatId
        };
    }
}

// View: 消息渲染(使用多方法Fluent API)
public class ExampleView : IView
{
    private readonly ITelegramBotClient _botClient;
    private long _chatId;
    private string _text;
    private List<InlineKeyboardButton> _buttons = new();

    public ExampleView(ITelegramBotClient botClient)
    {
        _botClient = botClient;
    }

    public ExampleView WithChatId(long chatId)
    {
        _chatId = chatId;
        return this;
    }

    public ExampleView WithText(string text)
    {
        _text = text;
        return this;
    }

    public ExampleView AddButton(string text, string callbackData)
    {
        _buttons.Add(InlineKeyboardButton.WithCallbackData(text, callbackData));
        return this;
    }

    public async Task Render()
    {
        await _botClient.SendMessage(
            chatId: _chatId,
            text: _text,
            replyMarkup: new InlineKeyboardMarkup(_buttons));
    }
}
```

## 关键规则
1. **Controller**:
   - 必须await所有异步方法
   - 提取基本数据传递给View
   - 协调Service和View交互

2. **Service**:
   - 实现IService接口
   - 处理业务逻辑
   - 返回处理结果

3. **View**:
   - 使用多个方法设置不同数据
   - 方法参数使用基本类型
   - Render必须是异步方法
   - 使用SendMessage发送消息
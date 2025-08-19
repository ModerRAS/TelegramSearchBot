using System;
using System.Reflection;
using Telegram.Bot.Types;

// 探索Telegram.Bot.Types.Message的构造函数和属性
class Program
{
    static void Main()
    {
        Console.WriteLine("=== Telegram.Bot.Types.Message 类分析 ===");
        
        Type messageType = typeof(Message);
        
        // 获取所有构造函数
        Console.WriteLine("\n--- 构造函数 ---");
        var constructors = messageType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        foreach (var constructor in constructors)
        {
            Console.WriteLine($"构造函数: {constructor}");
            var parameters = constructor.GetParameters();
            foreach (var param in parameters)
            {
                Console.WriteLine($"  参数: {param.ParameterType.Name} {param.Name}");
            }
        }
        
        // 获取所有属性
        Console.WriteLine("\n--- 属性 ---");
        var properties = messageType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            Console.WriteLine($"属性: {property.PropertyType.Name} {property.Name}");
            Console.WriteLine($"  可读: {property.CanRead}");
            Console.WriteLine($"  可写: {property.CanWrite}");
            if (property.CanWrite)
            {
                var setMethod = property.GetSetMethod();
                Console.WriteLine($"  Set方法: {setMethod}");
            }
            if (property.CanRead)
            {
                var getMethod = property.GetGetMethod();
                Console.WriteLine($"  Get方法: {getMethod}");
            }
            Console.WriteLine();
        }
        
        // 尝试创建Message实例
        Console.WriteLine("\n--- 创建Message实例 ---");
        try
        {
            var message = new Message();
            Console.WriteLine("无参构造函数成功创建Message实例");
            
            // 尝试设置MessageId
            var messageIdProperty = messageType.GetProperty("MessageId");
            if (messageIdProperty != null && messageIdProperty.CanWrite)
            {
                messageIdProperty.SetValue(message, 12345);
                Console.WriteLine($"成功设置MessageId: {messageIdProperty.GetValue(message)}");
            }
            else
            {
                Console.WriteLine("MessageId属性不可写");
                
                // 检查是否有init-only setter
                var setMethod = messageIdProperty?.GetSetMethod(true);
                if (setMethod != null)
                {
                    Console.WriteLine($"MessageId有Set方法，但访问级别为: {(setMethod.IsPublic ? "Public" : setMethod.IsAssembly ? "Internal" : setMethod.IsFamily ? "Protected" : "Private")}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"创建Message实例失败: {ex.Message}");
        }
        
        // 检查特定属性
        Console.WriteLine("\n--- 检查MessageId属性详细信息 ---");
        var messageIdProp = messageType.GetProperty("MessageId");
        if (messageIdProp != null)
        {
            Console.WriteLine($"MessageId属性类型: {messageIdProp.PropertyType}");
            Console.WriteLine($"MessageId可读: {messageIdProp.CanRead}");
            Console.WriteLine($"MessageId可写: {messageIdProp.CanWrite}");
            
            // 检查是否有init访问器
            var setMethod = messageIdProp.GetSetMethod(true);
            if (setMethod != null)
            {
                Console.WriteLine($"MessageId Set方法访问级别: {(setMethod.IsPublic ? "Public" : setMethod.IsAssembly ? "Internal" : setMethod.IsFamily ? "Protected" : "Private")}");
                Console.WriteLine($"Set方法返回类型: {setMethod.ReturnType}");
                
                // 检查是否是init-only
                var methodAttributes = setMethod.GetCustomAttributes();
                foreach (var attr in methodAttributes)
                {
                    Console.WriteLine($"Set方法特性: {attr.GetType().Name}");
                }
            }
            
            // 尝试通过对象初始化器设置
            Console.WriteLine("\n--- 尝试通过对象初始化器设置MessageId ---");
            try
            {
                var message2 = new Message 
                { 
                    MessageId = 54321,
                    Chat = new Chat { Id = 123 },
                    From = new User { Id = 456 },
                    Text = "Test message",
                    Date = DateTime.UtcNow
                };
                Console.WriteLine($"成功通过对象初始化器设置MessageId: {message2.MessageId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"通过对象初始化器设置MessageId失败: {ex.Message}");
            }
        }
        
        Console.WriteLine("\n=== 分析完成 ===");
    }
}
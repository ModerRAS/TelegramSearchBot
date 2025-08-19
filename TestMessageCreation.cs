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
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"创建Message实例失败: {ex.Message}");
        }
        
        // 尝试用有参构造函数创建
        Console.WriteLine("\n--- 尝试用有参构造函数创建 ---");
        try
        {
            // 查看是否有接受messageId的构造函数
            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                if (parameters.Length > 0)
                {
                    Console.WriteLine($"尝试构造函数: {constructor}");
                    // 这里我们不知道具体参数，所以跳过实际调用
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"有参构造函数创建失败: {ex.Message}");
        }
    }
}
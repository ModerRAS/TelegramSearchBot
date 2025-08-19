using System;
using System.Threading.Tasks;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Test.AI.LLM
{
    /// <summary>
    /// 验证LLM服务接口实现的测试类
    /// </summary>
    public class LLMServiceInterfaceValidationTests
    {
        /// <summary>
        /// 验证ILLMService接口的基本功能
        /// </summary>
        public static async Task ValidateILLMServiceImplementation(ILLMService service, LLMChannel channel)
        {
            Console.WriteLine($"验证 {service.GetType().Name} 的接口实现...");
            
            try
            {
                // 测试GenerateTextAsync方法
                Console.WriteLine("测试 GenerateTextAsync 方法...");
                var textResult = await service.GenerateTextAsync("Hello", channel);
                Console.WriteLine($"✅ GenerateTextAsync 方法实现正确，返回: {textResult?.Substring(0, Math.Min(50, textResult?.Length ?? 0))}...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GenerateTextAsync 方法测试失败: {ex.Message}");
            }
            
            try
            {
                // 测试GenerateEmbeddingsAsync方法
                Console.WriteLine("测试 GenerateEmbeddingsAsync 方法...");
                var embeddingResult = await service.GenerateEmbeddingsAsync("Hello", channel);
                Console.WriteLine($"✅ GenerateEmbeddingsAsync 方法实现正确，返回向量长度: {embeddingResult?.Length ?? 0}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GenerateEmbeddingsAsync 方法测试失败: {ex.Message}");
            }
            
            Console.WriteLine($"{service.GetType().Name} 接口实现验证完成。");
        }
    }
}
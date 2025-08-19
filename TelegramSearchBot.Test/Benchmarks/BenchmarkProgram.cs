using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;
using TelegramSearchBot.Benchmarks.Domain.Message;
using TelegramSearchBot.Benchmarks.Search;
using TelegramSearchBot.Benchmarks.Vector;

namespace TelegramSearchBot.Benchmarks
{
    /// <summary>
    /// 性能测试入口点
    /// 提供命令行接口来运行不同的性能测试套件
    /// </summary>
    public class BenchmarkProgram
    {
        /// <summary>
        /// 性能测试主入口
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>任务</returns>
        public static async Task Main(string[] args)
        {
            Console.WriteLine("🚀 TelegramSearchBot 性能测试套件");
            Console.WriteLine("=================================");
            Console.WriteLine();

            if (args.Length == 0)
            {
                ShowUsage();
                return;
            }

            var testType = args[0].ToLower();
            
            try
            {
                switch (testType)
                {
                    case "repository":
                    case "repo":
                        Console.WriteLine("📊 运行 MessageRepository 性能测试...");
                        await RunMessageRepositoryBenchmarks();
                        break;
                        
                    case "processing":
                    case "pipeline":
                        Console.WriteLine("⚙️ 运行 MessageProcessingPipeline 性能测试...");
                        await RunMessageProcessingBenchmarks();
                        break;
                        
                    case "search":
                    case "lucene":
                        Console.WriteLine("🔍 运行 Lucene 搜索性能测试...");
                        await RunSearchPerformanceBenchmarks();
                        break;
                        
                    case "vector":
                    case "faiss":
                        Console.WriteLine("🎯 运行 FAISS 向量搜索性能测试...");
                        await RunVectorSearchBenchmarks();
                        break;
                        
                    case "all":
                        Console.WriteLine("🔄 运行所有性能测试...");
                        await RunAllBenchmarks();
                        break;
                        
                    default:
                        Console.WriteLine($"❌ 未知的测试类型: {testType}");
                        ShowUsage();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 性能测试运行失败: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 运行MessageRepository性能测试
        /// </summary>
        private static async Task RunMessageRepositoryBenchmarks()
        {
            Console.WriteLine("🔬 测试场景:");
            Console.WriteLine("  - 小数据集查询 (100条)");
            Console.WriteLine("  - 中等数据集查询 (1,000条)");
            Console.WriteLine("  - 大数据集查询 (10,000条)");
            Console.WriteLine("  - 关键词搜索性能");
            Console.WriteLine("  - 插入/更新/删除操作");
            Console.WriteLine();
            
            BenchmarkRunner.Run<MessageRepositoryBenchmarks>();
            await Task.CompletedTask;
        }

        /// <summary>
        /// 运行MessageProcessingPipeline性能测试
        /// </summary>
        private static async Task RunMessageProcessingBenchmarks()
        {
            Console.WriteLine("🔬 测试场景:");
            Console.WriteLine("  - 单条消息处理");
            Console.WriteLine("  - 长消息处理");
            Console.WriteLine("  - 批量消息处理");
            Console.WriteLine("  - 不同内容类型 (中文/英文/特殊字符)");
            Console.WriteLine("  - 并发处理性能");
            Console.WriteLine("  - 内存分配测试");
            Console.WriteLine();
            
            BenchmarkRunner.Run<MessageProcessingBenchmarks>();
            await Task.CompletedTask;
        }

        /// <summary>
        /// 运行搜索性能测试
        /// </summary>
        private static async Task RunSearchPerformanceBenchmarks()
        {
            Console.WriteLine("🔬 测试场景:");
            Console.WriteLine("  - 简单关键词搜索");
            Console.WriteLine("  - 中文/英文搜索");
            Console.WriteLine("  - 语法搜索 (短语/字段指定/排除词)");
            Console.WriteLine("  - 分页搜索性能");
            Console.WriteLine("  - 索引构建性能");
            Console.WriteLine();
            
            // 注意：SearchPerformanceBenchmarks 实现了 IDisposable
            var benchmark = new SearchPerformanceBenchmarks();
            try
            {
                BenchmarkRunner.Run<SearchPerformanceBenchmarks>();
            }
            finally
            {
                benchmark.Dispose();
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// 运行向量搜索性能测试
        /// </summary>
        private static async Task RunVectorSearchBenchmarks()
        {
            Console.WriteLine("🔬 测试场景:");
            Console.WriteLine("  - 向量生成性能");
            Console.WriteLine("  - 相似性搜索");
            Console.WriteLine("  - 中文/英文向量搜索");
            Console.WriteLine("  - 余弦相似性计算");
            Console.WriteLine("  - TopK搜索性能");
            Console.WriteLine("  - 索引构建性能");
            Console.WriteLine();
            
            // 注意：VectorSearchBenchmarks 实现了 IDisposable
            var benchmark = new VectorSearchBenchmarks();
            try
            {
                BenchmarkRunner.Run<VectorSearchBenchmarks>();
            }
            finally
            {
                benchmark.Dispose();
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// 运行所有性能测试
        /// </summary>
        private static async Task RunAllBenchmarks()
        {
            Console.WriteLine("⚠️ 警告: 完整的性能测试套件可能需要较长时间运行");
            Console.WriteLine("建议分别运行各个测试套件以获得更详细的结果");
            Console.WriteLine();
            
            Console.WriteLine("1/4: MessageRepository 性能测试");
            await RunMessageRepositoryBenchmarks();
            
            Console.WriteLine("\n2/4: MessageProcessingPipeline 性能测试");
            await RunMessageProcessingBenchmarks();
            
            Console.WriteLine("\n3/4: Lucene 搜索性能测试");
            await RunSearchPerformanceBenchmarks();
            
            Console.WriteLine("\n4/4: FAISS 向量搜索性能测试");
            await RunVectorSearchBenchmarks();
            
            Console.WriteLine("\n✅ 所有性能测试完成!");
        }

        /// <summary>
        /// 显示使用说明
        /// </summary>
        private static void ShowUsage()
        {
            Console.WriteLine("📖 使用方法:");
            Console.WriteLine("  dotnet run --project TelegramSearchBot.Test -- <测试类型> [选项]");
            Console.WriteLine();
            Console.WriteLine("📋 测试类型:");
            Console.WriteLine("  repository, repo    - MessageRepository 性能测试");
            Console.WriteLine("  processing, pipeline - MessageProcessingPipeline 性能测试");
            Console.WriteLine("  search, lucene     - Lucene 搜索性能测试");
            Console.WriteLine("  vector, faiss      - FAISS 向量搜索性能测试");
            Console.WriteLine("  all                - 运行所有性能测试");
            Console.WriteLine();
            Console.WriteLine("🔧 环境要求:");
            Console.WriteLine("  - .NET 9.0 或更高版本");
            Console.WriteLine("  - BenchmarkDotNet 0.13.12");
            Console.WriteLine("  - 足够的内存和存储空间");
            Console.WriteLine();
            Console.WriteLine("📊 输出:");
            Console.WriteLine("  性测试结果将保存在当前目录的 BenchmarkDotNet.Artifacts 文件夹中");
            Console.WriteLine("  包含详细的性能指标、内存使用统计和图表");
            Console.WriteLine();
            Console.WriteLine("💡 提示:");
            Console.WriteLine("  - 建议在 Release 配置下运行以获得准确结果");
            Console.WriteLine("  - 关闭不必要的应用程序以减少系统干扰");
            Console.WriteLine("  - 大规模测试可能需要较长时间，请耐心等待");
        }
    }
}
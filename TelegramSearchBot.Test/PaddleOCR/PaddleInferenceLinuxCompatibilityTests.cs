using System;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace TelegramSearchBot.Test.PaddleOCR
{
    /// <summary>
    /// 测试 PaddleInference 在 Linux 上的兼容性
    /// 
    /// 原本实现：直接使用 PaddleOCR 进行 OCR 识别
    /// 简化实现：先验证运行时环境和依赖库的可用性，再测试基本功能
    /// 
    /// 这个测试的主要目的是验证当前项目配置在 Linux 上的问题，
    /// 特别是缺少 Linux 运行时包的问题。
    /// </summary>
    public class PaddleInferenceLinuxCompatibilityTests
    {
        private readonly ITestOutputHelper _output;

        public PaddleInferenceLinuxCompatibilityTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void TestOperatingSystem()
        {
            var os = RuntimeInformation.OSDescription;
            var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            
            _output.WriteLine($"当前操作系统: {os}");
            _output.WriteLine($"是否为 Linux: {isLinux}");
            _output.WriteLine($"是否为 Windows: {isWindows}");
            
            // 这个测试帮助我们了解当前的测试环境
            Assert.True(isLinux || isWindows, "不支持的操作系统");
        }

        [Fact]
        public void TestPaddleInferenceAssemblyLoading()
        {
            try
            {
                // 尝试加载 PaddleInference 程序集
                var assembly = System.Reflection.Assembly.GetAssembly(typeof(Sdcb.PaddleInference.PaddleDevice));
                Assert.NotNull(assembly);
                
                _output.WriteLine($"PaddleInference 程序集加载成功: {assembly.FullName}");
                _output.WriteLine($"程序集位置: {assembly.Location}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"PaddleInference 程序集加载失败: {ex.Message}");
                Assert.Fail($"PaddleInference 程序集加载失败: {ex.Message}");
            }
        }

        [Fact]
        public void TestPaddleDeviceCreation()
        {
            try
            {
                // 测试创建 PaddleDevice - 这是使用 PaddleInference 的基本操作
                var device = Sdcb.PaddleInference.PaddleDevice.Mkldnn();
                Assert.NotNull(device);
                
                _output.WriteLine($"PaddleDevice 创建成功: {device.GetType().Name}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"PaddleDevice 创建失败: {ex.Message}");
                _output.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                
                // 在 Linux 上，这里可能会失败，因为缺少对应的运行时库
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    _output.WriteLine("在 Linux 上失败可能是由于缺少 Linux 运行时包");
                    _output.WriteLine("需要添加包: Sdcb.PaddleInference.runtime.linux-x64.mkl");
                }
                
                // 这个测试预期在当前配置下可能会失败
                Assert.Fail($"PaddleDevice 创建失败: {ex.Message}");
            }
        }

        [Fact]
        public void TestPaddleOCRAvailability()
        {
            try
            {
                // 测试 PaddleOCR 相关的类型是否可用
                var ocrModelType = typeof(Sdcb.PaddleOCR.Models.Local.LocalFullModels);
                Assert.NotNull(ocrModelType);
                
                _output.WriteLine("PaddleOCR 模型类型加载成功");
                
                // 尝试获取中文模型信息
                var modelProperty = ocrModelType.GetProperty("ChineseV3");
                Assert.NotNull(modelProperty);
                
                _output.WriteLine("中文模型 V3 可用");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"PaddleOCR 可用性测试失败: {ex.Message}");
                Assert.Fail($"PaddleOCR 可用性测试失败: {ex.Message}");
            }
        }

        [Fact]
        public void TestNativeDependencyAvailability()
        {
            // 这个测试验证原生依赖库的可用性
            // 现在我们已经添加了 Linux 运行时包，这个测试应该能通过
            
            var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            
            if (isLinux)
            {
                _output.WriteLine("在 Linux 上测试原生依赖库可用性");
                _output.WriteLine("已添加的 Linux 运行时包：");
                _output.WriteLine("- Sdcb.PaddleInference.runtime.linux-x64.mkl");
                _output.WriteLine("- OpenCvSharp4.runtime.linux-x64");
                
                try
                {
                    // 尝试创建 PaddleDevice，这会加载原生库
                    var device = Sdcb.PaddleInference.PaddleDevice.Mkldnn();
                    Assert.NotNull(device);
                    
                    _output.WriteLine("Linux 原生依赖库加载成功！");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Linux 原生依赖库加载失败: {ex.Message}");
                    _output.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                    
                    // 即使添加了包，可能还有其他依赖问题
                    Assert.Fail($"Linux 原生依赖库加载失败: {ex.Message}");
                }
            }
            else
            {
                _output.WriteLine("不在 Linux 上，跳过原生依赖库测试");
                Assert.True(true, "跳过测试");
            }
        }

        [Fact]
        public void TestPaddleOCRInitialization()
        {
            // 这个测试实际尝试初始化 PaddleOCR，这是更全面的测试
            var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            
            _output.WriteLine($"测试 PaddleOCR 初始化 (平台: {(isLinux ? "Linux" : "Windows")})");
            
            try
            {
                // 尝试创建 PaddleOCR 实例，类似实际代码中的做法
                var model = Sdcb.PaddleOCR.Models.Local.LocalFullModels.ChineseV3;
                var device = Sdcb.PaddleInference.PaddleDevice.Mkldnn();
                
                var all = new Sdcb.PaddleOCR.PaddleOcrAll(model, device)
                {
                    AllowRotateDetection = true,
                    Enable180Classification = false,
                };
                
                Assert.NotNull(all);
                _output.WriteLine("PaddleOCR 初始化成功！");
                
                // 测试基本属性
                _output.WriteLine($"AllowRotateDetection: {all.AllowRotateDetection}");
                _output.WriteLine($"Enable180Classification: {all.Enable180Classification}");
                
            }
            catch (Exception ex)
            {
                _output.WriteLine($"PaddleOCR 初始化失败: {ex.Message}");
                _output.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                
                if (isLinux)
                {
                    _output.WriteLine("Linux 上的可能问题：");
                    _output.WriteLine("1. 缺少系统依赖库 (如 libgomp 等)");
                    _output.WriteLine("2. MKL 库兼容性问题");
                    _output.WriteLine("3. 权限问题");
                }
                
                Assert.Fail($"PaddleOCR 初始化失败: {ex.Message}");
            }
        }

        [Fact]
        public void ShowProjectConfigurationChanges()
        {
            _output.WriteLine("=== 项目配置变更记录 ===");
            _output.WriteLine("原始配置问题：");
            _output.WriteLine("1. RuntimeIdentifiers: win-x64;linux-x64");
            _output.WriteLine("2. 已安装的运行时包: Sdcb.PaddleInference.runtime.win64.mkl");
            _output.WriteLine("3. 缺少的运行时包: Sdcb.PaddleInference.runtime.linux-x64.mkl");
            _output.WriteLine("");
            _output.WriteLine("已实施的解决方案：");
            _output.WriteLine("✓ 添加了 Sdcb.PaddleInference.runtime.linux-x64.mkl");
            _output.WriteLine("✓ 添加了 OpenCvSharp4.runtime.linux-x64");
            _output.WriteLine("");
            _output.WriteLine("测试目标：");
            _output.WriteLine("- 验证 Linux 上的 PaddleInference 原生库加载");
            _output.WriteLine("- 测试 PaddleOCR 基本初始化");
            _output.WriteLine("- 识别可能的系统依赖问题");
            
            // 这个测试总是通过，只是用来显示配置变更
            Assert.True(true, "配置变更信息显示完成");
        }
    }
}
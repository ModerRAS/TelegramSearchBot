#pragma warning disable CS8602 // 解引用可能出现空引用
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using TelegramSearchBot.Service.AI.LLM;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Model;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM
{
    // --- Helper Classes/Methods for Testing ---
    public class TestToolProvider
    {
        public static bool StaticMethodCalled { get; set; } = false;
        public bool InstanceMethodCalled { get; set; } = false;
        public static string? LastStaticArg { get; set; }
        public string? LastInstanceArg { get; set; }
        public static int LastStaticIntArg { get; set; }
        public bool LastInstanceBoolArg { get; set; }
        public static string TestDecodeCommand = @"
```xml
<tool name=""ProcessMemoryCommandAsync"">
  <parameters>
    <command>add_observations</command>
    <arguments><![CDATA[
{
  ""observations"": [
    {
      ""entityName"": ""多模态推理测试用例_01"",
      ""contents"": [
        ""新增压力测试场景：模拟500并发推理请求"",
        ""加入异常记忆回溯测试模块"",
        ""集成对抗性推理干扰因子""
      ]
    }
  ]
}
]]></arguments>
    <toolContext>current_chat</toolContext>
  </parameters>
</tool>
```

```xml
<tool name=""ProcessThoughtAsync"">
  <parameters>
    <toolContext>current_chat</toolContext>
    <input>启动压力测试协议：加载分布式推理负载，注入随机噪声干扰...</input>
    <nextThoughtNeeded>true</nextThoughtNeeded>
    <thoughtNumber>2</thoughtNumber>
    <totalThoughts>4</totalThoughts>
    <branchFromThought>1</branchFromThought>
    <branchId>stress_test</branchId>
    <needsMoreThoughts>true</needsMoreThoughts>
  </parameters>
</tool>
```

```xml
<tool name=""ProcessMemoryCommandAsync"">
  <parameters>
    <command>create_relations</command>
    <arguments><![CDATA[
{
  ""relations"": [
    {
      ""from"": ""异常记忆回溯测试模块"",
      ""to"": ""记忆增强系统v2.3"",
      ""relationType"": ""压力测试""
    },
    {
      ""from"": ""对抗性推理干扰因子"",
      ""to"": ""认知架构HH-2025"",
      ""relationType"": ""韧性验证""
    }
  ]
}
]]></arguments>
    <toolContext>current_chat</toolContext>
  </parameters>
</tool>
```

```xml
<tool name=""ProcessThoughtAsync"">
  <parameters>
    <toolContext>current_chat</toolContext>
    <input>检测到推理延迟波动，启动自适应调节机制：动态调整神经符号权重比例...</input>
    <nextThoughtNeeded>true</nextThoughtNeeded>
    <thoughtNumber>3</thoughtNumber>
    <totalThoughts>5</totalThoughts>
    <isRevision>true</isRevision>
    <revisesThought>2</revisesThought>
    <branchId>adaptive_adjustment</branchId>
  </parameters>
</tool>
```
        
        ";

        [McpTool("A simple static tool.")]
        public static string StaticTool(
            [McpParameter("A string argument.")] string arg1,
            [McpParameter("An optional int.", IsRequired = false)] int arg2 = 5)
        {
            StaticMethodCalled = true;
            LastStaticArg = arg1;
            LastStaticIntArg = arg2;
            return $"Static received: {arg1}, {arg2}";
        }

        [McpTool("An instance tool.")]
        public virtual bool InstanceTool( // Ensure virtual
             [McpParameter("A required boolean.")] bool input)
        {
            InstanceMethodCalled = true;
            LastInstanceBoolArg = input;
            return !input;
        }

        [McpTool("An async instance tool.")]
        public virtual async Task<string> InstanceToolAsync( // Ensure virtual
            [McpParameter("Some text.")] string text)
        {
            await Task.Delay(1); // Simulate async work
            InstanceMethodCalled = true;
            LastInstanceArg = text;
            return $"Async processed: {text}";
        }
        
        // Tool with complex parameter (for JSON test)
         public class ComplexParam
         {
             public string? Name { get; set; }
             public int Value { get; set; }

             // Added for proper comparison in Moq.It.Is<T>
             public override bool Equals(object? obj)
             {
                 return obj is ComplexParam other &&
                        EqualityComparer<string?>.Default.Equals(Name, other.Name) &&
                        Value == other.Value;
             }

             public override int GetHashCode()
             {
                 return HashCode.Combine(Name, Value);
             }
         }
         [McpTool("Tool with complex parameter.")]
         public virtual string ComplexParamTool([McpParameter("Complex object.")] ComplexParam data) // Ensure virtual
         {
             return $"Complex: {data?.Name} = {data?.Value}";
         }

        // Method *not* marked as a tool
        public void NotATool() { }

        // Tool with conflicting name (should be ignored)
        [McpTool("Another static tool.", Name = "StaticTool")]
        public static void DuplicateStaticTool() { }

        // Dummy tool for TryParseToolCalls_ShouldParseSimpleToolCall
        [McpTool("A dummy tool for testing parsing.")]
        public static void TestTool(string param1, int param2) { }

        // Dummy tool for TryParseToolCalls_ShouldParseCDataContent and TryParseToolCalls_ShouldParseComplexMultiToolCommand
        [McpTool("A dummy memory command tool.")]
        public static void ProcessMemoryCommandAsync(string command, string arguments, string toolContext) { }

        // Dummy tool for TryParseToolCalls_ShouldParseComplexMultiToolCommand
        [McpTool("A dummy thought processing tool.")]
        public static void ProcessThoughtAsync(
            [McpParameter("The context of the tool call.")] string toolContext,
            [McpParameter("The input for the thought process.")] string input,
            [McpParameter("Whether the next thought is needed.")] bool nextThoughtNeeded,
            [McpParameter("The current thought number.")] int thoughtNumber,
            [McpParameter("The total number of thoughts.")] int totalThoughts,
            [McpParameter("The thought number to branch from.", IsRequired = false)] int? branchFromThought = null,
            [McpParameter("The branch identifier.", IsRequired = false)] string? branchId = null,
            [McpParameter("Whether more thoughts are needed.", IsRequired = false)] bool needsMoreThoughts = false,
            [McpParameter("Whether this is a revision.", IsRequired = false)] bool isRevision = false,
            [McpParameter("The thought number this revises.", IsRequired = false)] int? revisesThought = null)
        { }
    }

    // --- Test Class ---
    public class McpToolHelperTests
    {
        #pragma warning disable CS8618 // 单元测试中字段会在初始化方法中赋值
        // 移除构造函数中的静态字段和 Mock 成员，改为在测试方法内部声明和设置
        // private Mock<ILogger> _mockLogger = null!;
        // private Mock<IServiceProvider> _mockServiceProvider = null!;
        // private Mock<TestToolProvider> _mockToolProviderInstance = null!;
        #pragma warning restore CS8618

        private Mock<ILogger> _mockLogger;
        private Mock<IServiceProvider> _mockServiceProvider;
        private Mock<TestToolProvider> _mockToolProviderInstance;

        public McpToolHelperTests()
        {
            _mockLogger = new Mock<ILogger>();
            _mockLogger.Setup(x => x.IsEnabled(LogLevel.Debug)).Returns(true);
            _mockLogger.Setup(x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.IsAny<object>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<object, Exception?, string>>()))
                .Callback<LogLevel, EventId, object, Exception?, Func<object, Exception?, string>>((level, eventId, state, exception, formatter) =>
                {
                    var message = formatter(state, exception);
                    Console.WriteLine($"[DEBUG] {message}");
                });

            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockToolProviderInstance = new Mock<TestToolProvider> { CallBase = true };
            
            // Explicitly setup the service provider to return the mocked instance
            _mockServiceProvider
                .Setup(sp => sp.GetService(typeof(TestToolProvider)))
                .Returns(_mockToolProviderInstance.Object);

            // Setup mock instance methods before initialization
            _mockToolProviderInstance.Setup(x => x.InstanceTool(It.IsAny<bool>()))
                .Returns((bool input) => !input);
            _mockToolProviderInstance.Setup(x => x.InstanceToolAsync(It.IsAny<string>()))
                .Returns((string text) => Task.FromResult($"Async processed: {text}"));
            _mockToolProviderInstance.Setup(x => x.ComplexParamTool(It.IsAny<TestToolProvider.ComplexParam>()))
                .Returns((TestToolProvider.ComplexParam data) => $"Complex: {data?.Name} = {data?.Value}");

            // Only initialize once if not already initialized
            McpToolHelper.EnsureInitialized(typeof(TestToolProvider).Assembly, _mockServiceProvider.Object, _mockLogger.Object);
        }

        [Fact]
        public void CleanLlmResponse_RemovesThinkTags()
        {
            var raw = "Some text <think>This is thinking</think> more text.";
            var expected = "Some text more text.";
            var actual = McpToolHelper.CleanLlmResponse(raw);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CleanLlmResponse_RemovesMultipleThinkTags()
        {
            var raw = "<think>First thought.</think>Response<think>Second thought\nmulti-line</think>";
            var expected = "Response";
            var actual = McpToolHelper.CleanLlmResponse(raw);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CleanLlmResponse_HandlesNoThinkTags()
        {
            var raw = "Just plain text.";
            var expected = "Just plain text.";
            var actual = McpToolHelper.CleanLlmResponse(raw);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CleanLlmResponse_HandlesEmptyInput()
        {
            Assert.Equal("", McpToolHelper.CleanLlmResponse(""));
            Assert.Null(McpToolHelper.CleanLlmResponse(null));
        }

        [Fact]
        public void CleanLlmResponse_TrimsResult()
        {
            var raw = "  <think> pensée </think>   Result   ";
            var expected = "Result";
            var actual = McpToolHelper.CleanLlmResponse(raw);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryParseToolCalls_DirectParams_ParsesSingleToolCorrectly()
        {
            var xml = "<StaticTool><arg1>hello</arg1><arg2>10</arg2></StaticTool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.True(result);
            Assert.Single(parsedToolCalls);
            var firstTool = parsedToolCalls.First();
            Assert.Equal("StaticTool", firstTool.toolName);
            Assert.Equal(2, firstTool.arguments.Count);
            Assert.Equal("hello", firstTool.arguments["arg1"]);
            Assert.Equal("10", firstTool.arguments["arg2"]);
        }

        [Fact]
        public void TryParseToolCalls_NestedParams_ParsesSingleToolCorrectly()
        {
            var xml = "<tool name=\"InstanceTool\"><parameters><parameter name=\"input\">true</parameter></parameters></tool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.True(result);
            Assert.Single(parsedToolCalls);
            var firstTool = parsedToolCalls.First();
            Assert.Equal("InstanceTool", firstTool.toolName);
            Assert.Equal(1, firstTool.arguments.Count);
            Assert.Equal("true", firstTool.arguments["input"]);
        }
        
        [Fact]
        public void TryParseToolCalls_NestedParams_HandlesMissingParametersTagForSingleTool()
        {
            var xml = "<tool name=\"InstanceTool\"><parameter name=\"input\">false</parameter></tool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.True(result); 
            Assert.Single(parsedToolCalls);
            var firstTool = parsedToolCalls.First();
            Assert.Equal("InstanceTool", firstTool.toolName);
            Assert.Equal(1, firstTool.arguments.Count);
            Assert.Equal("false", firstTool.arguments["input"]);
        }

        [Fact]
        public void TryParseToolCalls_InvalidXml_ReturnsFalse()
        {
            var xml = "<StaticTool><arg1>hello</arg1"; // Malformed
            bool result = McpToolHelper.TryParseToolCalls(xml, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParseToolCalls_UnregisteredTool_ReturnsFalseOrEmpty() 
        {
            var xml = "<NotARealTool><arg1>hello</arg1></NotARealTool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);
            
            Assert.False(parsedToolCalls.Any());
            Assert.False(result);
        }
        
        [Fact]
        public void TryParseToolCalls_WithMarkdownFences_ParsesSingleToolCorrectly()
        {
            var xml = "```xml\n<StaticTool><arg1>fenced</arg1></StaticTool>\n```";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.True(result);
            Assert.Single(parsedToolCalls);
            var firstTool = parsedToolCalls.First();
            Assert.Equal("StaticTool", firstTool.toolName);
            Assert.Equal(1, firstTool.arguments.Count);
            Assert.Equal("fenced", firstTool.arguments["arg1"]);
        }

        [Fact]
        public void TryParseToolCalls_MultipleRootElements_ParsesAll()
        {
            var xml = "<StaticTool><arg1>first</arg1></StaticTool><InstanceTool><input>true</input></InstanceTool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.True(result);
            Assert.Equal(2, parsedToolCalls.Count);
            
            var firstTool = parsedToolCalls.FirstOrDefault(t => t.toolName == "StaticTool");
            Assert.NotNull(firstTool);
            Assert.Equal("StaticTool", firstTool.toolName);
            Assert.Equal(1, firstTool.arguments.Count);
            Assert.Equal("first", firstTool.arguments["arg1"]);

            var secondTool = parsedToolCalls.FirstOrDefault(t => t.toolName == "InstanceTool");
            Assert.NotNull(secondTool);
            Assert.Equal("InstanceTool", secondTool.toolName);
            Assert.Equal(1, secondTool.arguments.Count);
            Assert.Equal("true", secondTool.arguments["input"]);
        }
        
        [Fact]
        public void TryParseToolCalls_MultipleNestedToolElements_ParsesAll()
        {
            var xml = "<tools_wrapper><tool name=\"StaticTool\"><parameters><arg1>val1</arg1></parameters></tool><tool name=\"InstanceTool\"><parameters><input>true</input></parameters></tool></tools_wrapper>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.True(result);
            Assert.Equal(2, parsedToolCalls.Count);

            Assert.Equal("StaticTool", parsedToolCalls[0].toolName);
            Assert.Equal("val1", parsedToolCalls[0].arguments["arg1"]);
            
            Assert.Equal("InstanceTool", parsedToolCalls[1].toolName);
            Assert.Equal("true", parsedToolCalls[1].arguments["input"]);
        }

        // 新增测试方法
        [Fact]
        public void TryParseToolCalls_ShouldParseSimpleToolCall()
        {
            // Arrange
            var mockLogger = new Mock<ILogger>();
            var mockServiceProvider = new Mock<IServiceProvider>();

            // Initialize McpToolHelper
            McpToolHelper.EnsureInitialized(Assembly.GetExecutingAssembly(), mockServiceProvider.Object, mockLogger.Object);

            string input = @"<tool name=""TestTool"">
                <parameters>
                    <param1>value1</param1>
                    <param2>123</param2>
                </parameters>
            </tool>";

            // Act
            bool result = McpToolHelper.TryParseToolCalls(input, out var parsedToolCalls);

            // Assert
            Assert.True(result);
            Assert.Single(parsedToolCalls);
            var toolCall = parsedToolCalls.First();
            // Removed Assert.NotNull(toolCall) as it's a value type tuple
            Assert.Equal("TestTool", toolCall.toolName);
            Assert.Equal(2, toolCall.arguments.Count);
            Assert.Equal("value1", toolCall.arguments["param1"]);
            Assert.Equal("123", toolCall.arguments["param2"]);
        }

        [Fact]
        public void TryParseToolCalls_ShouldParseCDataContent()
        {
            // Arrange
            var mockLogger = new Mock<ILogger>();
            var mockServiceProvider = new Mock<IServiceProvider>();

            // Initialize McpToolHelper
            McpToolHelper.EnsureInitialized(Assembly.GetExecutingAssembly(), mockServiceProvider.Object, mockLogger.Object);

            string input = @"<tool name=""ProcessMemoryCommandAsync"">
                <parameters>
                    <command>add_observations</command>
                    <arguments><![CDATA[
{
    ""observations"": [
        {
            ""entityName"": ""测试实体"",
            ""contents"": [""内容1"", ""内容2""]
        }
    ]
}
]]></arguments>
                </parameters>
            </tool>";

            // Act
            bool result = McpToolHelper.TryParseToolCalls(input, out var parsedToolCalls);

            // Assert
            Assert.True(result);
            Assert.Single(parsedToolCalls);
            var args = parsedToolCalls[0].arguments;
            Assert.Equal("add_observations", args["command"]);
            Assert.Contains("\"entityName\": \"测试实体\"", args["arguments"]);
        }

        [Fact]
        public async Task ExecuteRegisteredToolAsync_StaticMethod_Executes()
        {
            // Arrange
            var args = new Dictionary<string, string> { { "arg1", "test static" }, { "arg2", "99" } };

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync("StaticTool", args);

            // Assert
            Assert.True(TestToolProvider.StaticMethodCalled);
            Assert.Equal("test static", TestToolProvider.LastStaticArg);
            Assert.Equal(99, TestToolProvider.LastStaticIntArg);
            Assert.Equal("Static received: test static, 99", result);
        }
        
        [Fact]
        public async Task ExecuteRegisteredToolAsync_StaticMethod_UsesDefaultParam()
        {
            // Arrange
            var args = new Dictionary<string, string> { { "arg1", "default test" } }; // Omit arg2

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync("StaticTool", args);

            // Assert
            Assert.True(TestToolProvider.StaticMethodCalled);
            Assert.Equal("default test", TestToolProvider.LastStaticArg);
            Assert.Equal(5, TestToolProvider.LastStaticIntArg); // Default value
            Assert.Equal("Static received: default test, 5", result);
        }

        [Fact]
        public async Task ExecuteRegisteredToolAsync_InstanceMethod_ViaDI_Executes()
        {
            // Arrange
            // Create local mocks for this test
            var localMockLogger = new Mock<ILogger>();
            var localMockServiceProvider = new Mock<IServiceProvider>();
            var localMockToolProviderInstance = new Mock<TestToolProvider> { CallBase = true };

            // Setup the local service provider to return the local mocked instance
            localMockServiceProvider.Setup(sp => sp.GetService(typeof(TestToolProvider)))
                .Returns(localMockToolProviderInstance.Object);

            // Use reflection to reset the static initialized flag in McpToolHelper
            var isInitializedField = typeof(McpToolHelper).GetField("_sIsInitialized", BindingFlags.NonPublic | BindingFlags.Static);
            isInitializedField.SetValue(null, false); // Set static field value

            // Re-initialize McpToolHelper with local mocks
            McpToolHelper.EnsureInitialized(typeof(TestToolProvider).Assembly, localMockServiceProvider.Object, localMockLogger.Object);

            // Setup mock instance methods on the local mock instance specific to this test
            localMockToolProviderInstance.Setup(x => x.InstanceTool(It.IsAny<bool>()))
                .Returns((bool input) => !input);

            var toolName = "InstanceTool";
            var parameters = new Dictionary<string, string> { { "input", "false" } };

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync(toolName, parameters);

            // Assert
            // Verify the method call on the local mock instance
            localMockToolProviderInstance.Verify(p => p.InstanceTool(false), Times.Once());
            // Verify the return value
            Assert.Equal("True", result.ToString()); // InstanceTool returns !input, so if input is false, it returns true.
        }
        
        [Fact]
        public async Task ExecuteRegisteredToolAsync_InstanceMethod_ViaActivator_Executes()
        {
            // Arrange
            // Create local mocks for this test
            var localMockLogger = new Mock<ILogger>();
            var localMockServiceProvider = new Mock<IServiceProvider>();
            var localMockToolProviderInstance = new Mock<TestToolProvider> { CallBase = true };

            // Setup the local service provider to return the local mocked instance
            localMockServiceProvider.Setup(sp => sp.GetService(typeof(TestToolProvider)))
                .Returns(localMockToolProviderInstance.Object);

            // Use reflection to reset the static initialized flag in McpToolHelper
            var isInitializedField = typeof(McpToolHelper).GetField("_sIsInitialized", BindingFlags.NonPublic | BindingFlags.Static);
            isInitializedField.SetValue(null, false); // Set static field value

            // Re-initialize McpToolHelper with local mocks
            McpToolHelper.EnsureInitialized(typeof(TestToolProvider).Assembly, localMockServiceProvider.Object, localMockLogger.Object);

            // Setup mock instance methods on the local mock instance specific to this test
            localMockToolProviderInstance.Setup(x => x.InstanceTool(It.IsAny<bool>()))
                .Returns((bool input) => !input);

            var toolName = "InstanceTool";
            var parameters = new Dictionary<string, string> { { "input", "true" } };

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync(toolName, parameters);

            // Assert
            // Verify the method call on the local mock instance
            localMockToolProviderInstance.Verify(p => p.InstanceTool(true), Times.Once());
            // Verify the return value
            Assert.Equal("False", result.ToString()); // InstanceTool returns !input, so if input is true, it returns false.
        }

        [Fact]
        public async Task ExecuteRegisteredToolAsync_AsyncInstanceMethod_Executes()
        {
            // Arrange
            // Create local mocks for this test
            var localMockLogger = new Mock<ILogger>();
            var localMockServiceProvider = new Mock<IServiceProvider>();
            var localMockToolProviderInstance = new Mock<TestToolProvider> { CallBase = true };

            // Setup the local service provider to return the local mocked instance
            localMockServiceProvider.Setup(sp => sp.GetService(typeof(TestToolProvider)))
                .Returns(localMockToolProviderInstance.Object);

            // Use reflection to reset the static initialized flag in McpToolHelper
            var isInitializedField = typeof(McpToolHelper).GetField("_sIsInitialized", BindingFlags.NonPublic | BindingFlags.Static);
            isInitializedField.SetValue(null, false); // Set static field value

            // Re-initialize McpToolHelper with local mocks
            McpToolHelper.EnsureInitialized(typeof(TestToolProvider).Assembly, localMockServiceProvider.Object, localMockLogger.Object);

            // Setup mock instance methods on the local mock instance specific to this test
            localMockToolProviderInstance.Setup(x => x.InstanceToolAsync(It.IsAny<string>()))
                .Returns((string text) => Task.FromResult($"Async processed: {text}"));

            var toolName = "InstanceToolAsync";
            var parameters = new Dictionary<string, string> { { "text", "async test" } };

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync(toolName, parameters);

            // Assert
            // Verify the method call on the local mock instance
            localMockToolProviderInstance.Verify(p => p.InstanceToolAsync("async test"), Times.Once());
            // Verify the return value
            Assert.Equal("Async processed: async test", result);
        }
        
        [Fact]
        public async Task ExecuteRegisteredToolAsync_ComplexParam_DeserializesAndExecutes()
        {
            // Arrange
            // Create local mocks for this test
            var localMockLogger = new Mock<ILogger>();
            var localMockServiceProvider = new Mock<IServiceProvider>();
            var localMockToolProviderInstance = new Mock<TestToolProvider> { CallBase = true };

            // Setup the local service provider to return the local mocked instance
            localMockServiceProvider.Setup(sp => sp.GetService(typeof(TestToolProvider)))
                .Returns(localMockToolProviderInstance.Object);

            // Use reflection to reset the static initialized flag in McpToolHelper
            var isInitializedField = typeof(McpToolHelper).GetField("_sIsInitialized", BindingFlags.NonPublic | BindingFlags.Static);
            isInitializedField.SetValue(null, false); // Set static field value

            // Re-initialize McpToolHelper with local mocks
            McpToolHelper.EnsureInitialized(typeof(TestToolProvider).Assembly, localMockServiceProvider.Object, localMockLogger.Object);

            // Setup mock instance methods on the local mock instance specific to this test
            localMockToolProviderInstance.Setup(x => x.ComplexParamTool(It.IsAny<TestToolProvider.ComplexParam>()))
                .Returns((TestToolProvider.ComplexParam data) => $"Complex: {data?.Name} = {data?.Value}");

            var toolName = "ComplexParamTool";
            // Note: Complex parameters are expected to be passed as JSON string values within the dictionary.
            var complexParamJson = "{\"Name\": \"Widget\", \"Value\": 123}";
            var parameters = new Dictionary<string, string> { { "data", complexParamJson } };

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync(toolName, parameters);

            // Assert
            // Verify the method call on the local mock instance
            // For complex parameters, Moq needs to match the deserialized object.
            // We use It.Is<T> with a predicate to compare the properties.
            localMockToolProviderInstance.Verify(p => p.ComplexParamTool(It.Is<TestToolProvider.ComplexParam>(cp => cp.Name == "Widget" && cp.Value == 123)), Times.Once());
            // Verify the return value
            Assert.Equal("Complex: Widget = 123", result);
        }

        [Fact]
        public async Task ExecuteRegisteredToolAsync_MissingRequiredArg_ThrowsArgumentException()
        {
            // Arrange
            var args = new Dictionary<string, string>(); // Missing arg1 for StaticTool

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => McpToolHelper.ExecuteRegisteredToolAsync("StaticTool", args)
            );
        }

        [Fact]
        public async Task ExecuteRegisteredToolAsync_UnregisteredTool_ThrowsArgumentException()
        {
            // Arrange
            var args = new Dictionary<string, string>();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => McpToolHelper.ExecuteRegisteredToolAsync("NonExistentTool", args)
            );
        }

        [Fact]
        public void TryParseToolCalls_ShouldParseComplexMultiToolCommand()
        {
            // Arrange
            var input = TestToolProvider.TestDecodeCommand;
            // 移除工具字符串分割处理，直接使用原始输入测试

            // Act & Assert - Parse all tools together first
            bool result = McpToolHelper.TryParseToolCalls(input, out var parsedToolCalls);
            Assert.True(result);
            Assert.Equal(4, parsedToolCalls.Count);

            // Then verify each tool's details
            foreach (var (toolName, arguments) in parsedToolCalls)
            {
                Assert.NotNull(toolName);
                Assert.NotNull(arguments);
                // 移除对固定参数的断言，改为检查特定工具的参数
            }

            // Verify first tool (ProcessMemoryCommandAsync)
            Assert.Equal("ProcessMemoryCommandAsync", parsedToolCalls[0].toolName);
            Assert.Equal("add_observations", parsedToolCalls[0].arguments["command"]);
            Assert.Equal("current_chat", parsedToolCalls[0].arguments["toolContext"]);
            Assert.Contains("\"entityName\": \"多模态推理测试用例_01\"", parsedToolCalls[0].arguments["arguments"]);

            // Verify second tool (ProcessThoughtAsync)
            Assert.Equal("ProcessThoughtAsync", parsedToolCalls[1].toolName);
            Assert.Equal("current_chat", parsedToolCalls[1].arguments["toolContext"]);
            Assert.Equal("启动压力测试协议：加载分布式推理负载，注入随机噪声干扰...", parsedToolCalls[1].arguments["input"]);
            Assert.Equal("true", parsedToolCalls[1].arguments["nextThoughtNeeded"]);

            // Verify third tool (ProcessMemoryCommandAsync)
            Assert.Equal("ProcessMemoryCommandAsync", parsedToolCalls[2].toolName);
            Assert.Equal("create_relations", parsedToolCalls[2].arguments["command"]);
            Assert.Contains("\"from\": \"异常记忆回溯测试模块\"", parsedToolCalls[2].arguments["arguments"]);

            // Verify fourth tool (ProcessThoughtAsync)
            Assert.Equal("ProcessThoughtAsync", parsedToolCalls[3].toolName);
            Assert.Equal("检测到推理延迟波动，启动自适应调节机制：动态调整神经符号权重比例...", parsedToolCalls[3].arguments["input"]);
            Assert.Equal("true", parsedToolCalls[3].arguments["isRevision"]);
        }
    }
}

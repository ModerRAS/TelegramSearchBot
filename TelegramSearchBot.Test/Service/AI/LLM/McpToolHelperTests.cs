#pragma warning disable CS8602 // 解引用可能出现空引用
using Microsoft.Extensions.Logging;
using Moq;
using System;
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
         public class ComplexParam { public string? Name { get; set; } public int Value { get; set; } }
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
        private Mock<ILogger> _mockLogger = null!;
        private Mock<IServiceProvider> _mockServiceProvider = null!;
        private Mock<TestToolProvider> _mockToolProviderInstance = null!;
        #pragma warning restore CS8618

        public McpToolHelperTests()
        {
            McpToolHelper.ResetForTest();
            _mockLogger = new Mock<ILogger>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockToolProviderInstance = new Mock<TestToolProvider> { CallBase = true };
            _mockServiceProvider.Setup(sp => sp.GetService(typeof(TestToolProvider))).Returns(_mockToolProviderInstance.Object);
            McpToolHelper.EnsureInitialized(typeof(TestToolProvider).Assembly, _mockServiceProvider.Object, _mockLogger.Object);

            // Setup mock instance methods
            _mockToolProviderInstance.Setup(x => x.InstanceTool(It.IsAny<bool>()))
                .Returns((bool input) => !input);
            _mockToolProviderInstance.Setup(x => x.InstanceToolAsync(It.IsAny<string>()))
                .Returns((string text) => Task.FromResult($"Async processed: {text}"));
            _mockToolProviderInstance.Setup(x => x.ComplexParamTool(It.IsAny<TestToolProvider.ComplexParam>()))
                .Returns((TestToolProvider.ComplexParam data) => $"Complex: {data?.Name} = {data?.Value}");

            // Reset static flags before each test
            TestToolProvider.StaticMethodCalled = false;
            TestToolProvider.LastStaticArg = null;
            TestToolProvider.LastStaticIntArg = 0;
            // Reset mock instance state
            _mockToolProviderInstance.Object.InstanceMethodCalled = false;
            _mockToolProviderInstance.Object.LastInstanceArg = null;
            _mockToolProviderInstance.Object.LastInstanceBoolArg = false;
            _mockToolProviderInstance.Invocations.Clear();
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
            Assert.Equal(1, parsedToolCalls.Count);
            var firstTool = parsedToolCalls[0];
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
            Assert.Equal(1, parsedToolCalls.Count);
            var firstTool = parsedToolCalls[0];
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
            Assert.Equal(1, parsedToolCalls.Count);
            var firstTool = parsedToolCalls[0];
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
            Assert.Equal(1, parsedToolCalls.Count);
            var firstTool = parsedToolCalls[0];
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
            var assembly = Assembly.GetExecutingAssembly();
            McpToolHelper.EnsureInitialized(assembly, _mockServiceProvider.Object, _mockLogger.Object);

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
            Assert.Equal(1, parsedToolCalls.Count);
            Assert.Equal("TestTool", parsedToolCalls[0].toolName);
            Assert.Equal("value1", parsedToolCalls[0].arguments["param1"]);
            Assert.Equal("123", parsedToolCalls[0].arguments["param2"]);
        }

        [Fact]
        public void TryParseToolCalls_ShouldParseCDataContent()
        {
            // Arrange
            var assembly = Assembly.GetExecutingAssembly();
            McpToolHelper.EnsureInitialized(assembly, _mockServiceProvider.Object, _mockLogger.Object);

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
            Assert.Equal(1, parsedToolCalls.Count);
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
            var args = new Dictionary<string, string> { { "input", "false" } };
            _mockToolProviderInstance.Setup(p => p.InstanceTool(false)).Returns(true);

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync("InstanceTool", args);

            // Assert
            _mockToolProviderInstance.Verify(p => p.InstanceTool(false), Times.Once);
            Assert.Equal(true, result);
        }
        
        [Fact]
        public async Task ExecuteRegisteredToolAsync_InstanceMethod_ViaActivator_Executes()
        {
            // Arrange
            var args = new Dictionary<string, string> { { "input", "true" } };
            _mockToolProviderInstance.Setup(p => p.InstanceTool(true)).Returns(false);
            
            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync("InstanceTool", args);

            // Assert
            Assert.Equal(false, result); 
            _mockToolProviderInstance.Verify(p => p.InstanceTool(true), Times.AtLeastOnce()); 
        }

        [Fact]
        public async Task ExecuteRegisteredToolAsync_AsyncInstanceMethod_Executes()
        {
            // Arrange
            var args = new Dictionary<string, string> { { "text", "async test" } };
            _mockToolProviderInstance.Setup(p => p.InstanceToolAsync("async test"))
                                     .ReturnsAsync("Async processed: async test");

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync("InstanceToolAsync", args);

            // Assert
            _mockToolProviderInstance.Verify(p => p.InstanceToolAsync("async test"), Times.Once);
            Assert.Equal("Async processed: async test", result);
        }
        
        [Fact]
        public async Task ExecuteRegisteredToolAsync_ComplexParam_DeserializesAndExecutes()
        {
            // Arrange
            var complexJson = "{\"Name\":\"Widget\",\"Value\":123}";
            var args = new Dictionary<string, string> { { "data", complexJson } };
            _mockToolProviderInstance.Setup(p => p.ComplexParamTool(It.Is<TestToolProvider.ComplexParam>(cp => cp.Name == "Widget" && cp.Value == 123)))
                                     .Returns("Complex: Widget = 123");

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync("ComplexParamTool", args);

            // Assert
            _mockToolProviderInstance.Verify(p => p.ComplexParamTool(It.Is<TestToolProvider.ComplexParam>(cp => cp.Name == "Widget" && cp.Value == 123)), Times.Once);
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

            // Act
            bool result = McpToolHelper.TryParseToolCalls(input, out var parsedToolCalls);

            // Assert
            Assert.True(result);
            Assert.Equal(4, parsedToolCalls.Count);

            // Verify first tool (ProcessMemoryCommandAsync)
            var firstTool = parsedToolCalls[0];
            Assert.Equal("ProcessMemoryCommandAsync", firstTool.toolName);
            Assert.Equal("add_observations", firstTool.arguments["command"]);
            Assert.Equal("current_chat", firstTool.arguments["toolContext"]);
            Assert.Contains("\"entityName\": \"多模态推理测试用例_01\"", firstTool.arguments["arguments"]);

            // Verify second tool (ProcessThoughtAsync)
            var secondTool = parsedToolCalls[1];
            Assert.Equal("ProcessThoughtAsync", secondTool.toolName);
            Assert.Equal("current_chat", secondTool.arguments["toolContext"]);
            Assert.Equal("启动压力测试协议：加载分布式推理负载，注入随机噪声干扰...", secondTool.arguments["input"]);
            Assert.Equal("true", secondTool.arguments["nextThoughtNeeded"]);

            // Verify third tool (ProcessMemoryCommandAsync)
            var thirdTool = parsedToolCalls[2];
            Assert.Equal("ProcessMemoryCommandAsync", thirdTool.toolName);
            Assert.Equal("create_relations", thirdTool.arguments["command"]);
            Assert.Contains("\"from\": \"异常记忆回溯测试模块\"", thirdTool.arguments["arguments"]);

            // Verify fourth tool (ProcessThoughtAsync)
            var fourthTool = parsedToolCalls[3];
            Assert.Equal("ProcessThoughtAsync", fourthTool.toolName);
            Assert.Equal("检测到推理延迟波动，启动自适应调节机制：动态调整神经符号权重比例...", fourthTool.arguments["input"]);
            Assert.Equal("true", fourthTool.arguments["isRevision"]);
        }
    }
}

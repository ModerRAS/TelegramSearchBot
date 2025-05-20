using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using TelegramSearchBot.Service.AI.LLM;
using Microsoft.Extensions.DependencyInjection;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Test.Service.AI.LLM
{
    // --- Helper Classes/Methods for Testing ---
    public class TestToolProvider
    {
        public static bool StaticMethodCalled { get; set; } = false;
        public bool InstanceMethodCalled { get; set; } = false;
        public static string LastStaticArg { get; set; }
        public string LastInstanceArg { get; set; }
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
         public class ComplexParam { public string Name { get; set; } public int Value { get; set; } }
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
            [McpParameter("The branch identifier.", IsRequired = false)] string branchId = null,
            [McpParameter("Whether more thoughts are needed.", IsRequired = false)] bool needsMoreThoughts = false,
            [McpParameter("Whether this is a revision.", IsRequired = false)] bool isRevision = false,
            [McpParameter("The thought number this revises.", IsRequired = false)] int? revisesThought = null)
        { }
    }

    // --- Test Class ---
    [TestClass]
    public class McpToolHelperTests
    {
        private static Mock<ILogger> _mockLogger;
        private static Mock<IServiceProvider> _mockServiceProvider;
        private static Mock<TestToolProvider> _mockToolProviderInstance;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            _mockLogger = new Mock<ILogger>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockToolProviderInstance = new Mock<TestToolProvider>(); // Create a mock instance

            // Setup ServiceProvider mock to return the mock instance when requested
            _mockServiceProvider.Setup(sp => sp.GetService(typeof(TestToolProvider)))
                                .Returns(_mockToolProviderInstance.Object);

            // Initialize McpToolHelper once for all tests in this class
            McpToolHelper.EnsureInitialized(typeof(TestToolProvider).Assembly, _mockServiceProvider.Object, _mockLogger.Object);
        }

        [TestInitialize]
        public void TestInitialize()
        {
             // Reset static flags before each test
             TestToolProvider.StaticMethodCalled = false;
             TestToolProvider.LastStaticArg = null;
             TestToolProvider.LastStaticIntArg = 0;
             // Reset mock instance state if needed (Moq resets automatically usually)
             _mockToolProviderInstance.Object.InstanceMethodCalled = false;
             _mockToolProviderInstance.Object.LastInstanceArg = null;
             _mockToolProviderInstance.Object.LastInstanceBoolArg = false;
             _mockToolProviderInstance.Invocations.Clear(); // Clear invocation tracking
        }

        [TestMethod]
        public void CleanLlmResponse_RemovesThinkTags()
        {
            var raw = "Some text <think>This is thinking</think> more text.";
            var expected = "Some text more text.";
            var actual = McpToolHelper.CleanLlmResponse(raw);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void CleanLlmResponse_RemovesMultipleThinkTags()
        {
            var raw = "<think>First thought.</think>Response<think>Second thought\nmulti-line</think>";
            var expected = "Response";
            var actual = McpToolHelper.CleanLlmResponse(raw);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void CleanLlmResponse_HandlesNoThinkTags()
        {
            var raw = "Just plain text.";
            var expected = "Just plain text.";
            var actual = McpToolHelper.CleanLlmResponse(raw);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void CleanLlmResponse_HandlesEmptyInput()
        {
            Assert.AreEqual("", McpToolHelper.CleanLlmResponse(""));
            Assert.IsNull(McpToolHelper.CleanLlmResponse(null));
        }

        [TestMethod]
        public void CleanLlmResponse_TrimsResult()
        {
            var raw = "  <think> pensée </think>   Result   ";
            var expected = "Result";
            var actual = McpToolHelper.CleanLlmResponse(raw);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void TryParseToolCalls_DirectParams_ParsesSingleToolCorrectly()
        {
            var xml = "<StaticTool><arg1>hello</arg1><arg2>10</arg2></StaticTool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.IsTrue(result);
            Assert.AreEqual(1, parsedToolCalls.Count);
            var firstTool = parsedToolCalls[0];
            Assert.AreEqual("StaticTool", firstTool.toolName);
            Assert.AreEqual(2, firstTool.arguments.Count);
            Assert.AreEqual("hello", firstTool.arguments["arg1"]);
            Assert.AreEqual("10", firstTool.arguments["arg2"]);
        }

        [TestMethod]
        public void TryParseToolCalls_NestedParams_ParsesSingleToolCorrectly()
        {
            var xml = "<tool name=\"InstanceTool\"><parameters><parameter name=\"input\">true</parameter></parameters></tool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.IsTrue(result);
            Assert.AreEqual(1, parsedToolCalls.Count);
            var firstTool = parsedToolCalls[0];
            Assert.AreEqual("InstanceTool", firstTool.toolName);
            Assert.AreEqual(1, firstTool.arguments.Count);
            Assert.AreEqual("true", firstTool.arguments["input"]);
        }
        
        [TestMethod]
        public void TryParseToolCalls_NestedParams_HandlesMissingParametersTagForSingleTool()
        {
            var xml = "<tool name=\"InstanceTool\"><parameter name=\"input\">false</parameter></tool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.IsTrue(result); 
            Assert.AreEqual(1, parsedToolCalls.Count);
            var firstTool = parsedToolCalls[0];
            Assert.AreEqual("InstanceTool", firstTool.toolName);
            Assert.AreEqual(1, firstTool.arguments.Count);
            Assert.AreEqual("false", firstTool.arguments["input"]);
        }

        [TestMethod]
        public void TryParseToolCalls_InvalidXml_ReturnsFalse()
        {
            var xml = "<StaticTool><arg1>hello</arg1"; // Malformed
            bool result = McpToolHelper.TryParseToolCalls(xml, out _);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParseToolCalls_UnregisteredTool_ReturnsFalseOrEmpty() 
        {
            var xml = "<NotARealTool><arg1>hello</arg1></NotARealTool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);
            
            Assert.IsFalse(parsedToolCalls.Any());
            Assert.IsFalse(result);
        }
        
        [TestMethod]
        public void TryParseToolCalls_WithMarkdownFences_ParsesSingleToolCorrectly()
        {
            var xml = "```xml\n<StaticTool><arg1>fenced</arg1></StaticTool>\n```";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.IsTrue(result);
            Assert.AreEqual(1, parsedToolCalls.Count);
            var firstTool = parsedToolCalls[0];
            Assert.AreEqual("StaticTool", firstTool.toolName);
            Assert.AreEqual(1, firstTool.arguments.Count);
            Assert.AreEqual("fenced", firstTool.arguments["arg1"]);
        }

        [TestMethod]
        public void TryParseToolCalls_MultipleRootElements_ParsesFirstTool()
        {
            var xml = "<StaticTool><arg1>first</arg1></StaticTool><InstanceTool><input>true</input></InstanceTool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.IsTrue(result);
            Assert.AreEqual(2, parsedToolCalls.Count);
            
            var firstTool = parsedToolCalls.FirstOrDefault(t => t.toolName == "StaticTool");
            Assert.IsNotNull(firstTool);
            Assert.AreEqual("StaticTool", firstTool.toolName);
            Assert.AreEqual(1, firstTool.arguments.Count);
            Assert.AreEqual("first", firstTool.arguments["arg1"]);

            var secondTool = parsedToolCalls.FirstOrDefault(t => t.toolName == "InstanceTool");
            Assert.IsNotNull(secondTool);
            Assert.AreEqual("InstanceTool", secondTool.toolName);
            Assert.AreEqual(1, secondTool.arguments.Count);
            Assert.AreEqual("true", secondTool.arguments["input"]);
        }
        
        [TestMethod]
        public void TryParseToolCalls_MultipleNestedToolElements_ParsesAll()
        {
            var xml = "<tools_wrapper><tool name=\"StaticTool\"><parameters><arg1>val1</arg1></parameters></tool><tool name=\"InstanceTool\"><parameters><input>true</input></parameters></tool></tools_wrapper>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.IsTrue(result);
            Assert.AreEqual(2, parsedToolCalls.Count);

            Assert.AreEqual("StaticTool", parsedToolCalls[0].toolName);
            Assert.AreEqual("val1", parsedToolCalls[0].arguments["arg1"]);
            
            Assert.AreEqual("InstanceTool", parsedToolCalls[1].toolName);
            Assert.AreEqual("true", parsedToolCalls[1].arguments["input"]);
        }

        // 新增测试方法
        [TestMethod]
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
            Assert.IsTrue(result);
            Assert.AreEqual(1, parsedToolCalls.Count);
            Assert.AreEqual("TestTool", parsedToolCalls[0].toolName);
            Assert.AreEqual("value1", parsedToolCalls[0].arguments["param1"]);
            Assert.AreEqual("123", parsedToolCalls[0].arguments["param2"]);
        }

        [TestMethod]
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
            Assert.IsTrue(result);
            Assert.AreEqual(1, parsedToolCalls.Count);
            var args = parsedToolCalls[0].arguments;
            Assert.AreEqual("add_observations", args["command"]);
            Assert.IsTrue(args["arguments"].Contains("\"entityName\": \"测试实体\""));
        }

        [TestMethod]
        public async Task ExecuteRegisteredToolAsync_StaticMethod_Executes()
        {
            // Arrange
            var args = new Dictionary<string, string> { { "arg1", "test static" }, { "arg2", "99" } };

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync("StaticTool", args);

            // Assert
            Assert.IsTrue(TestToolProvider.StaticMethodCalled);
            Assert.AreEqual("test static", TestToolProvider.LastStaticArg);
            Assert.AreEqual(99, TestToolProvider.LastStaticIntArg);
            Assert.AreEqual("Static received: test static, 99", result);
        }
        
        [TestMethod]
        public async Task ExecuteRegisteredToolAsync_StaticMethod_UsesDefaultParam()
        {
            // Arrange
            var args = new Dictionary<string, string> { { "arg1", "default test" } }; // Omit arg2

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync("StaticTool", args);

            // Assert
            Assert.IsTrue(TestToolProvider.StaticMethodCalled);
            Assert.AreEqual("default test", TestToolProvider.LastStaticArg);
            Assert.AreEqual(5, TestToolProvider.LastStaticIntArg); // Default value
            Assert.AreEqual("Static received: default test, 5", result);
        }

        [TestMethod]
        public async Task ExecuteRegisteredToolAsync_InstanceMethod_ViaDI_Executes()
        {
            // Arrange
            var args = new Dictionary<string, string> { { "input", "false" } };
            _mockToolProviderInstance.Setup(p => p.InstanceTool(false)).Returns(true);

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync("InstanceTool", args);

            // Assert
            _mockToolProviderInstance.Verify(p => p.InstanceTool(false), Times.Once);
            Assert.AreEqual(true, result);
        }
        
        [TestMethod]
        public async Task ExecuteRegisteredToolAsync_InstanceMethod_ViaActivator_Executes()
        {
             // Arrange
             var args = new Dictionary<string, string> { { "input", "true" } };
             
             // Act
             var result = await McpToolHelper.ExecuteRegisteredToolAsync("InstanceTool", args);

             // Assert
             _mockToolProviderInstance.Setup(p => p.InstanceTool(true)).Returns(false); 
             Assert.AreEqual(false, result); 
             _mockToolProviderInstance.Verify(p => p.InstanceTool(true), Times.AtLeastOnce()); 
        }

        [TestMethod]
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
            Assert.AreEqual("Async processed: async test", result);
        }
        
        [TestMethod]
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
            Assert.AreEqual("Complex: Widget = 123", result);
        }

        [TestMethod]
        public async Task ExecuteRegisteredToolAsync_MissingRequiredArg_ThrowsArgumentException()
        {
            // Arrange
            var args = new Dictionary<string, string>(); // Missing arg1 for StaticTool

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => McpToolHelper.ExecuteRegisteredToolAsync("StaticTool", args),
                "Missing required parameter 'arg1'"
            );
        }

        [TestMethod]
        public async Task ExecuteRegisteredToolAsync_UnregisteredTool_ThrowsArgumentException()
        {
            // Arrange
            var args = new Dictionary<string, string>();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => McpToolHelper.ExecuteRegisteredToolAsync("NonExistentTool", args),
                "Tool 'NonExistentTool' not registered"
            );
        }

        [TestMethod]
        public void TryParseToolCalls_ShouldParseComplexMultiToolCommand()
        {
            // Arrange
            var input = TestToolProvider.TestDecodeCommand;

            // Act
            bool result = McpToolHelper.TryParseToolCalls(input, out var parsedToolCalls);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(4, parsedToolCalls.Count);

            // Verify first tool (ProcessMemoryCommandAsync)
            var firstTool = parsedToolCalls[0];
            Assert.AreEqual("ProcessMemoryCommandAsync", firstTool.toolName);
            Assert.AreEqual("add_observations", firstTool.arguments["command"]);
            Assert.AreEqual("current_chat", firstTool.arguments["toolContext"]);
            Assert.IsTrue(firstTool.arguments["arguments"].Contains("\"entityName\": \"多模态推理测试用例_01\""));

            // Verify second tool (ProcessThoughtAsync)
            var secondTool = parsedToolCalls[1];
            Assert.AreEqual("ProcessThoughtAsync", secondTool.toolName);
            Assert.AreEqual("current_chat", secondTool.arguments["toolContext"]);
            Assert.AreEqual("启动压力测试协议：加载分布式推理负载，注入随机噪声干扰...", secondTool.arguments["input"]);
            Assert.AreEqual("true", secondTool.arguments["nextThoughtNeeded"]);

            // Verify third tool (ProcessMemoryCommandAsync)
            var thirdTool = parsedToolCalls[2];
            Assert.AreEqual("ProcessMemoryCommandAsync", thirdTool.toolName);
            Assert.AreEqual("create_relations", thirdTool.arguments["command"]);
            Assert.IsTrue(thirdTool.arguments["arguments"].Contains("\"from\": \"异常记忆回溯测试模块\""));

            // Verify fourth tool (ProcessThoughtAsync)
            var fourthTool = parsedToolCalls[3];
            Assert.AreEqual("ProcessThoughtAsync", fourthTool.toolName);
            Assert.AreEqual("检测到推理延迟波动，启动自适应调节机制：动态调整神经符号权重比例...", fourthTool.arguments["input"]);
            Assert.AreEqual("true", fourthTool.arguments["isRevision"]);
        }
    }
}

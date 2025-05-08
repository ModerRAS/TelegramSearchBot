using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using TelegramSearchBot.Service.AI.LLM;
using Microsoft.Extensions.DependencyInjection;

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
            McpToolHelper.Initialize(_mockServiceProvider.Object, _mockLogger.Object);
            // Register tools from the test provider class
            McpToolHelper.RegisterToolsAndGetPromptString(typeof(TestToolProvider).Assembly);
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
        public void TryParseToolCall_DirectParams_ParsesCorrectly()
        {
            var xml = "<StaticTool><arg1>hello</arg1><arg2>10</arg2></StaticTool>";
            bool result = McpToolHelper.TryParseToolCall(xml, out string toolName, out var args);

            Assert.IsTrue(result);
            Assert.AreEqual("StaticTool", toolName);
            Assert.AreEqual(2, args.Count);
            Assert.AreEqual("hello", args["arg1"]);
            Assert.AreEqual("10", args["arg2"]);
        }

        [TestMethod]
        public void TryParseToolCall_NestedParams_ParsesCorrectly()
        {
            var xml = "<tool name=\"InstanceTool\"><parameters><parameter name=\"input\">true</parameter></parameters></tool>";
             bool result = McpToolHelper.TryParseToolCall(xml, out string toolName, out var args);

            Assert.IsTrue(result);
            Assert.AreEqual("InstanceTool", toolName);
            Assert.AreEqual(1, args.Count);
            Assert.AreEqual("true", args["input"]);
        }
        
        [TestMethod]
        public void TryParseToolCall_NestedParams_HandlesMissingParametersTag()
        {
            // Although our prompt shows <parameters>, test robustness if LLM omits it
            var xml = "<tool name=\"InstanceTool\"><parameter name=\"input\">false</parameter></tool>";
             bool result = McpToolHelper.TryParseToolCall(xml, out string toolName, out var args);

            Assert.IsTrue(result); // Should still find tool name
             Assert.AreEqual("InstanceTool", toolName);
             // Current logic requires <parameters> tag for the <tool name="..."> format.
             // If <parameters> is missing, paramsContainer will be null, and the loop won't run.
             Assert.AreEqual(0, args.Count); 
        }

        [TestMethod]
        public void TryParseToolCall_InvalidXml_ReturnsFalse()
        {
            var xml = "<StaticTool><arg1>hello</arg1"; // Malformed
            bool result = McpToolHelper.TryParseToolCall(xml, out _, out _);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParseToolCall_UnregisteredTool_ReturnsFalse()
        {
            var xml = "<NotARealTool><arg1>hello</arg1></NotARealTool>";
            bool result = McpToolHelper.TryParseToolCall(xml, out _, out _);
            Assert.IsFalse(result);
        }
        
        [TestMethod]
        public void TryParseToolCall_WithMarkdownFences_ParsesCorrectly()
        {
            var xml = "```xml\n<StaticTool><arg1>fenced</arg1></StaticTool>\n```";
            bool result = McpToolHelper.TryParseToolCall(xml, out string toolName, out var args);

            Assert.IsTrue(result);
            Assert.AreEqual("StaticTool", toolName);
            Assert.AreEqual(1, args.Count);
            Assert.AreEqual("fenced", args["arg1"]);
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
            // Setup the mock to return the expected value
            _mockToolProviderInstance.Setup(p => p.InstanceTool(false)).Returns(true); // !false = true

            // Act
            var result = await McpToolHelper.ExecuteRegisteredToolAsync("InstanceTool", args);

            // Assert
            // Verify the method was called on the *mock* instance provided by DI
             _mockToolProviderInstance.Verify(p => p.InstanceTool(false), Times.Once);
             Assert.AreEqual(true, result); // Check the return value setup in Arrange
        }
        
        [TestMethod]
        public async Task ExecuteRegisteredToolAsync_InstanceMethod_ViaActivator_Executes()
        {
             // Arrange
             var args = new Dictionary<string, string> { { "input", "true" } };
             // Reset ServiceProvider mock to *not* return an instance, forcing Activator.CreateInstance
             var localServiceProviderMock = new Mock<IServiceProvider>();
             localServiceProviderMock.Setup(sp => sp.GetService(typeof(TestToolProvider))).Returns(null);
             McpToolHelper.Initialize(localServiceProviderMock.Object, _mockLogger.Object); // Re-initialize helper for this test
             
             // Act
             var result = await McpToolHelper.ExecuteRegisteredToolAsync("InstanceTool", args);

             // Assert
             // We can't easily verify calls on the Activator-created instance, but we check the result
             Assert.AreEqual(false, result); // !true = false

             // Restore original initializer for other tests
             McpToolHelper.Initialize(_mockServiceProvider.Object, _mockLogger.Object); 
        }

        [TestMethod]
        public async Task ExecuteRegisteredToolAsync_AsyncInstanceMethod_Executes()
        {
            // Arrange
            var args = new Dictionary<string, string> { { "text", "async test" } };
            // Setup the mock method call
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
                "Missing required parameter 'arg1'" // Check exception message contains parameter name
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
                "Tool 'NonExistentTool' not registered."
            );
        }
    }
}

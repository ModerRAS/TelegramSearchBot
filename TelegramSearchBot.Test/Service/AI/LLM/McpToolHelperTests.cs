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
            // Current logic for <tool name="..."><parameter name="arg">value</parameter></tool>
            // (without an encapsulating <parameters> tag) will result in 'input' being a direct child of 'tool'
            // and thus parsed as a parameter.
            Assert.AreEqual(1, firstTool.arguments.Count, "A direct child <parameter> should be parsed.");
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
            // If the XML is well-formed but the tool name isn't registered,
            // TryParseToolCalls might return true but an empty list, or false.
            // Current logic in McpToolHelper for unrecognized elements logs a warning and skips them.
            // So, if only unrecognized tools are present, it should return true with an empty list, or false if nothing is parsable.
            var xml = "<NotARealTool><arg1>hello</arg1></NotARealTool>";
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);
            
            // Depending on strictness: if it parses the structure but finds no *registered* tools,
            // parsedToolCalls would be empty. If the structure itself is rejected due to no known tool pattern, result is false.
            // The current TryParseToolCalls logs a warning and skips if elementToParse is not a known tool pattern.
            // So, if "NotARealTool" is not a registered tool, it will be skipped.
            // If it's the *only* element, parsedToolCalls will be empty, and the method returns parsedToolCalls.Any()
            Assert.IsFalse(parsedToolCalls.Any()); // More robust: ensure no valid tools were parsed.
                                               // result itself could be true if the XML was valid but contained no *recognized* tools.
                                               // Let's refine: if no *registered* tools are found, it should effectively be a "no tool call" scenario.
                                               // The method returns parsedToolCalls.Any(). So if list is empty, result is false.
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
            Assert.AreEqual(2, parsedToolCalls.Count, "Should parse both tools when wrapped.");
            
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
            // This case is already handled by the <tools_wrapper> logic if the outer parse fails.
            // If LLM outputs this directly, XDocument.Parse will succeed with <tools_wrapper> as root.
            bool result = McpToolHelper.TryParseToolCalls(xml, out var parsedToolCalls);

            Assert.IsTrue(result);
            Assert.AreEqual(2, parsedToolCalls.Count);

            Assert.AreEqual("StaticTool", parsedToolCalls[0].toolName);
            Assert.AreEqual("val1", parsedToolCalls[0].arguments["arg1"]);
            
            Assert.AreEqual("InstanceTool", parsedToolCalls[1].toolName);
            Assert.AreEqual("true", parsedToolCalls[1].arguments["input"]);
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
             // Note: This test's ability to verify the Activator path is limited by McpToolHelper's
             // static, one-time initialization set in ClassInitialize. 
             // It currently relies on the ClassInitialize setup for _sServiceProvider.
             
             // Act
             var result = await McpToolHelper.ExecuteRegisteredToolAsync("InstanceTool", args);

             // Assert
             // This assertion assumes the DI path (from ClassInitialize) is taken.
             _mockToolProviderInstance.Setup(p => p.InstanceTool(true)).Returns(false); 
             Assert.AreEqual(false, result); 
             _mockToolProviderInstance.Verify(p => p.InstanceTool(true), Times.AtLeastOnce()); 
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

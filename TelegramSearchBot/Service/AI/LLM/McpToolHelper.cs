using System;
using System.Collections.Concurrent; // Added for ConcurrentDictionary
using TelegramSearchBot.Model; // Added for ToolContext
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Xml.Linq;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection; // For potential DI later
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json; // Added for Json.NET
using System.Text.RegularExpressions; // Added for Regex cleaning
using TelegramSearchBot.Attributes; // Added to reference McpToolAttribute and McpParameterAttribute
using TelegramSearchBot.Service.Tools; // For DuckDuckGoSearchResult

namespace TelegramSearchBot.Service.AI.LLM
{
    /// <summary>
    /// Formats a list of methods (decorated with McpToolAttribute) into a string for the LLM prompt.
    /// </summary>
    public static class McpToolHelper
    {
        private static readonly ConcurrentDictionary<string, (MethodInfo Method, Type OwningType)> ToolRegistry = new ConcurrentDictionary<string, (MethodInfo, Type)>();
        private static IServiceProvider _sServiceProvider; // For DI-based instance resolution
        private static ILogger _sLogger;
        private static string _sCachedToolsXml;
        private static bool _sIsInitialized = false;
        private static readonly object _initializationLock = new object();

        private static void Initialize(IServiceProvider serviceProvider, ILogger logger)
        {
            _sServiceProvider = serviceProvider;
            _sLogger = logger;
        }

        /// <summary>
        /// Ensures that McpToolHelper is initialized, tools are registered, and the prompt string is cached.
        /// This method should be called once at application startup.
        /// </summary>
        public static void EnsureInitialized(Assembly assembly, IServiceProvider serviceProvider, ILogger logger)
        {
            if (_sIsInitialized)
            {
                return;
            }

            lock (_initializationLock)
            {
                if (_sIsInitialized)
                {
                    return;
                }

                Initialize(serviceProvider, logger); // Sets _sServiceProvider and _sLogger

                _sLogger?.LogInformation("McpToolHelper.EnsureInitialized: Starting tool registration...");
                _sCachedToolsXml = RegisterToolsAndGetPromptString(assembly);
                if (string.IsNullOrWhiteSpace(_sCachedToolsXml))
                {
                    _sCachedToolsXml = "<!-- No tools are currently available. -->";
                    _sLogger?.LogWarning("McpToolHelper.EnsureInitialized: No tools found or registered. Prompt will indicate no tools available.");
                }
                else
                {
                    _sLogger?.LogInformation("McpToolHelper.EnsureInitialized: Tools registered and XML prompt cached successfully.");
                }
                
                _sIsInitialized = true;
            }
        }
        
        /// <summary>
        /// Scans an assembly for methods marked with McpToolAttribute and registers them.
        /// Also generates the descriptive string for the LLM prompt.
        /// This method is called by EnsureInitialized.
        /// </summary>
        private static string RegisterToolsAndGetPromptString(Assembly assembly)
        {
            ToolRegistry.Clear(); // Clear previous registrations if any
            
            var loggerForRegistration = _sLogger; 

            var methods = assembly.GetTypes()
                                  .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                                  .Where(m => m.GetCustomAttribute<McpToolAttribute>() != null)
                                  .ToList();
            
            var sb = new StringBuilder();
            foreach (var method in methods)
            {
                var toolAttr = method.GetCustomAttribute<McpToolAttribute>();
                if (toolAttr == null) continue; // Should not happen due to Where clause

                var toolName = string.IsNullOrWhiteSpace(toolAttr.Name) ? method.Name : toolAttr.Name;
                toolName = toolName.Split('`')[0]; // Sanitize

                // Use TryAdd for ConcurrentDictionary to handle race conditions more gracefully if multiple threads were to register the exact same tool name simultaneously,
                // though the Clear() at the beginning makes this less of an issue for distinct calls to RegisterToolsAndGetPromptString.
                // However, the primary issue is concurrent calls to RegisterToolsAndGetPromptString itself, each operating on the shared static ToolRegistry.
                if (!ToolRegistry.TryAdd(toolName, (method, method.DeclaringType)))
                {
                    // If TryAdd fails, it means the key already exists. This check is slightly different from ContainsKey + Add,
                    // but given the Clear() at the start of this method, if this method is called sequentially,
                    // ContainsKey would be false. If called concurrently, TryAdd is safer.
                    // The original log message for duplicate is still relevant if different methods map to the same sanitized toolName.
                    loggerForRegistration?.LogWarning($"Duplicate tool name '{toolName}' found or failed to add. Method {method.DeclaringType.FullName}.{method.Name} might be ignored or overwritten by a concurrent call.");
                    // If we want to strictly prevent overwriting and log the original "ignored" message,
                    // we might need a slightly different approach, but ConcurrentDictionary handles the concurrent access safely.
                    // For simplicity and to address the core concurrency bug, TryAdd is sufficient.
                    // If it was already added by another concurrent call, this instance of the tool might not be the one stored.
                    // The duplicate tool name warnings from the user log suggest this is a valid concern.
                    // Let's stick to a pattern closer to the original to ensure the warning logic remains:
                if (ToolRegistry.ContainsKey(toolName)) // Check first
                {
                     loggerForRegistration?.LogWarning($"Duplicate tool name '{toolName}' found. Method {method.DeclaringType.FullName}.{method.Name} will be ignored.");
                     continue;
                }
                // If, after the check, another thread adds it, ToolRegistry[toolName] could throw or overwrite.
                // So, TryAdd is indeed better. Let's re-evaluate.
                // The goal is: if toolName is new, add it. If it exists, log warning and skip.
                // This needs to be atomic.
                var toolTuple = (method, method.DeclaringType);
                if (!ToolRegistry.TryAdd(toolName, toolTuple))
                {
                    // This means toolName was already present (added by this thread in a previous iteration for a *different* method mapping to the same name, or by another thread).
                    loggerForRegistration?.LogWarning($"Duplicate tool name '{toolName}' encountered for method {method.DeclaringType.FullName}.{method.Name}. This tool registration will be skipped.");
                    continue;
                }
                // If TryAdd succeeded, it's in the dictionary.
            }
            // ToolRegistry[toolName] = (method, method.DeclaringType); // This line is now handled by TryAdd

            sb.AppendLine($"- <tool name=\"{toolName}\">");
            sb.AppendLine($"    <description>{toolAttr.Description}</description>");
                sb.AppendLine($"    <parameters>");
                foreach (var param in method.GetParameters())
                {
                    var paramAttr = param.GetCustomAttribute<McpParameterAttribute>();
                    var paramDescription = paramAttr?.Description ?? $"Parameter '{param.Name}'";
                    var paramIsRequired = paramAttr?.IsRequired ?? (!param.IsOptional && !param.HasDefaultValue && !(param.ParameterType.IsValueType && Nullable.GetUnderlyingType(param.ParameterType) == null));
                    var paramType = GetSimplifiedTypeName(param.ParameterType);
                    sb.AppendLine($"        <parameter name=\"{param.Name}\" type=\"{paramType}\" required=\"{paramIsRequired.ToString().ToLower()}\">{paramDescription}</parameter>");
                }
                sb.AppendLine($"    </parameters>");
                sb.AppendLine($"  </tool>");
            }
            return sb.ToString();
        }

        /// <summary>
    /// Formats the standard system prompt incorporating tool descriptions and usage instructions.
    /// </summary>
    public static string FormatSystemPrompt(string botName, long chatId)
    {
         if (!_sIsInitialized)
         {
            _sLogger?.LogCritical("McpToolHelper.FormatSystemPrompt called before EnsureInitialized. Tool descriptions will be missing or incorrect.");
            // Consider throwing an InvalidOperationException here if this state is truly unrecoverable.
         }

         if (string.IsNullOrWhiteSpace(botName)) botName = "AI Assistant";
         string toolsXmlToUse = _sCachedToolsXml ?? "<!-- Tools not initialized or no tools available. -->";

         // Exact prompt structure from OpenAIService/OllamaService
         return $"你的名字是 {botName}，你是一个AI助手。现在时间是：{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")}。当前对话的群聊ID是:{chatId}。\n\n" + 
                $"你的核心任务是协助用户。为此，你可以调用外部工具。以下是你当前可以使用的工具列表和它们的描述：\n\n" +
                $"{toolsXmlToUse}\n\n" + 
                $"当你需要调用工具时，必须严格遵循以下规则：\n\n" +
                "1. 工具调用必须使用XML格式，且必须是回复的唯一内容\n" +
                "2. 每次只能调用一个工具，多个工具必须分多次调用\n" +
                "3. 工具调用格式示例：\n" +
                "```xml\n" +
                "<tool_name>\n" +
                "  <parameter1>value1</parameter1>\n" +
                "  <parameter2>value2</parameter2>\n" +
                "</tool_name>\n" +
                "```\n\n" +
                "4. 或者使用更结构化的格式：\n" +
                "```xml\n" +
                "<tool name=\"tool_name\">\n" +
                "  <parameters>\n" +
                "    <parameter1>value1</parameter1>\n" +
                "    <parameter2>value2</parameter2>\n" +
                "  </parameters>\n" +
                "</tool>\n" +
                "```\n\n" +
                "5. 关键要求：\n" +
                "- 确保工具名称和参数名称完全匹配\n" +
                "- 参数值必须正确格式化（字符串加引号，数字不加）\n" +
                "- 不要包含任何额外的解释或聊天内容\n" +
                "- 如果参数值包含特殊字符，确保正确转义\n" +
                "- 严格禁止在单个消息中调用多个工具\n\n" +
                "6. 工具调用流程：\n" +
                "- 每次只能使用一个工具\n" +
                "- 等待工具执行结果后再决定下一步操作\n" +
                "- 如果需要多个工具，必须分多次调用\n" +
                "- 工具调用之间必须等待用户确认执行结果\n\n" +
                    "重要提示：如果你调用一个工具（特别是搜索类工具）后没有找到你需要的信息，或者结果不理想，你可以尝试以下操作：\n" +
                    "1. 修改你的查询参数（例如，使用更宽泛或更具体的关键词，尝试不同的搜索选项等），然后再次调用同一个工具。\n" +
                    "2. 如果多次尝试仍不理想，或者你认为其他工具可能更合适，可以尝试调用其他可用工具。\n" +
                    "3. 在进行多次尝试时，建议在思考过程中记录并调整你的策略。\n" +
                    "如果你认为已经获得了足够的信息，或者不需要再使用工具，请继续下一步。\n\n" +
                    "引用说明：当你使用工具（特别是搜索工具）获取信息并在回答中使用了这些信息时，如果工具结果提供了来源(Source)或链接(URL)，请务必在你的回答中清晰地注明来源。你可以使用Markdown链接格式，例如 `[来源标题](URL)`，或者在回答末尾列出引用来源列表。确保用户可以追溯信息的原始出处。\n\n" +
                    $"在决定是否使用工具时，请仔细分析用户的请求。如果不需要工具，或者工具执行完毕后，请直接以自然语言回复用户。\n" +
                    $"当你直接回复时，请直接输出内容，不要模仿历史消息的格式。";
        }

        /// <summary>
        /// Removes thinking tags and trims whitespace from raw LLM response.
        /// </summary>
        /// <param name="rawResponse">The raw response string from the LLM.</param>
        /// <returns>The cleaned response string.</returns>
        public static string CleanLlmResponse(string rawResponse)
        {
            if (string.IsNullOrEmpty(rawResponse))
            {
                return rawResponse;
            }

            // Step 1: Remove <think>...</think> blocks
            string cleaned = Regex.Replace(
                rawResponse, 
                @"<think>.*?</think>", // Original tag removal
                "", 
                RegexOptions.Singleline | RegexOptions.IgnoreCase
            );
            
            // Step 2: Collapse multiple whitespace chars into a single space
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");
            // Removed extra parenthesis from previous line

            // Trim leading/trailing whitespace that might be left
            return cleaned.Trim();
        }
        
        public static bool TryParseToolCalls(string input, out List<(string toolName, Dictionary<string, string> arguments)> parsedToolCalls)
        {
            parsedToolCalls = new List<(string toolName, Dictionary<string, string> arguments)>();

            if (string.IsNullOrWhiteSpace(input))
            {
                _sLogger?.LogDebug("TryParseToolCalls: Input is null or empty");
                return false;
            }

            try
            {
                string processedInput = input.Trim();
                _sLogger?.LogDebug($"Original input: {processedInput}");

                // Handle Markdown code blocks first
                if (processedInput.Contains("```xml") || processedInput.Contains("```"))
                {
                    var codeBlocks = new List<string>();
                    var matches = Regex.Matches(processedInput,
                        @"```(?:xml)?\s*(<tool[\s\S]*?<\/tool>|<[\w]+>[\s\S]*?<\/[\w]+>)\s*```",
                        RegexOptions.Multiline);

                    foreach (Match m in matches)
                    {
                        var content = m.Groups[1].Value.Trim();
                        codeBlocks.Add(content);
                    }

                    if (codeBlocks.Count > 0)
                    {
                        // Combine all code block content for subsequent parsing
                        processedInput = string.Join("\n", codeBlocks);
                    }
                    else if (processedInput.Contains("<tool"))
                    {
                        // If it contains <tool> but no markdown, just trim
                        processedInput = processedInput.Trim();
                    }
                    else
                    {
                        _sLogger?.LogWarning("No valid XML content found in code blocks or as raw XML");
                        return false;
                    }
                }
                // If no markdown, check if it looks like raw XML
                else if (processedInput.Contains("<") && processedInput.Contains(">"))
                {
                    // Keep processedInput as is for raw XML parsing attempt
                }
                else
                {
                     _sLogger?.LogDebug("TryParseToolCalls: Input does not appear to be XML or markdown code block.");
                     return false;
                }

                // --- Improved Parsing Logic ---
                // First try to parse the input as a whole document
                try
                {
                    XDocument doc = XDocument.Parse(processedInput);
                    // Check if there's a wrapper element containing tool elements
                    if (doc.Root.Elements().Any(e =>
                        e.Name.LocalName.Equals("tool", StringComparison.OrdinalIgnoreCase) ||
                        ToolRegistry.ContainsKey(e.Name.LocalName)))
                    {
                        // Process each tool element inside the wrapper
                        foreach (var toolElement in doc.Root.Elements())
                        {
                            var toolCall = ParseToolElement(toolElement);
                            if (toolCall.toolName != null)
                            {
                                parsedToolCalls.Add(toolCall);
                            }
                        }
                        return parsedToolCalls.Count > 0;
                    }
                }
                catch (System.Xml.XmlException)
                {
                    // Fall through to regex parsing if XML parsing fails
                }

                // Fallback to regex parsing for individual tool blocks
                var toolBlockMatches = Regex.Matches(processedInput, @"<(tool\b[^>]*|[\w]+)(?:\s[^>]*)?>[\s\S]*?<\/\1>", RegexOptions.IgnoreCase);
                _sLogger?.LogInformation($"TryParseToolCalls: Found {toolBlockMatches.Count} tool elements in input: {processedInput}");
                _sLogger?.LogInformation($"TryParseToolCalls: Raw regex matches - Count: {toolBlockMatches.Count}");
                _sLogger?.LogInformation($"TryParseToolCalls: Using regex pattern: {@"<(tool\b[^>]*|[\w]+)(?:\s[^>]*)?>[\s\S]*?<\/\1>"}");
                for (int i = 0; i < toolBlockMatches.Count; i++)
                {
                    _sLogger?.LogInformation($"Match {i}: {toolBlockMatches[i].Value}");
                }

                _sLogger?.LogDebug($"TryParseToolCalls: Found {toolBlockMatches.Count} potential tool blocks using regex.");

                if (toolBlockMatches.Count == 0)
                {
                    _sLogger?.LogDebug("TryParseToolCalls: No potential tool blocks found after initial processing.");
                    return false;
                }

                foreach (Match match in toolBlockMatches)
                {
                    string blockXml = match.Value;
                    _sLogger?.LogDebug($"TryParseToolCalls: Processing block: {blockXml}");
                    try
                    {
                        // Attempt to parse the individual block XML
                        XDocument blockDoc = XDocument.Parse(blockXml, LoadOptions.PreserveWhitespace);
                        XElement blockRoot = blockDoc.Root;

                        // Use the existing ParseToolElement logic to extract tool name and arguments
                        var toolCall = ParseToolElement(blockRoot);
                        if (toolCall.toolName != null)
                        {
                            parsedToolCalls.Add(toolCall);
                            _sLogger?.LogDebug($"TryParseToolCalls: Successfully parsed tool '{toolCall.toolName}'. Current count: {parsedToolCalls.Count}");
                        } else {
                             _sLogger?.LogDebug($"TryParseToolCalls: ParseToolElement returned null toolName for block: {blockXml}");
                        }
                    }
                    catch (System.Xml.XmlException ex)
                    {
                        _sLogger?.LogError(ex, $"TryParseToolCalls: Failed to parse individual tool block XML: {blockXml}");
                        // Continue to the next block
                    }
                    catch (Exception ex)
                    {
                        _sLogger?.LogError(ex, $"TryParseToolCalls: Unexpected error processing tool block: {blockXml}");
                        // Continue to the next block
                    }
                }

                _sLogger?.LogDebug($"TryParseToolCalls: Final parsedToolCalls count: {parsedToolCalls.Count}");
                return parsedToolCalls.Count > 0; // Return true if any tools were parsed
            }
            catch (Exception ex)
            {
                _sLogger?.LogError(ex, "TryParseToolCalls: Unexpected error parsing tool calls");
                return false;
            }
        }

        private static (string toolName, Dictionary<string, string> arguments) ParseToolElement(XElement element)
        {
            string toolName = null;
            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 确定工具名称 - 更灵活的匹配逻辑
            if (element.Name.LocalName.Equals("tool", StringComparison.OrdinalIgnoreCase))
            {
                toolName = element.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(toolName))
                {
                    _sLogger?.LogWarning("ParseToolElement: Tool element has no name attribute");
                    return (null, null);
                }
            }
            else
            {
                // 尝试匹配注册的工具名称
                toolName = ToolRegistry.Keys.FirstOrDefault(k => 
                    k.Equals(element.Name.LocalName, StringComparison.OrdinalIgnoreCase));
            }

            if (toolName == null || !ToolRegistry.ContainsKey(toolName))
            {
                _sLogger?.LogWarning($"ParseToolElement: Unregistered tool '{element.Name.LocalName}'");
                return (null, null);
            }

            // 处理CDATA内容 - 保留原始CDATA标记
            foreach (var cdata in element.DescendantNodes().OfType<XCData>())
            {
                var parent = cdata.Parent;
                if (parent != null)
                {
                    arguments[parent.Name.LocalName] = cdata.Value;
                }
            }

            // 获取参数容器 - 更灵活的查找逻辑
            XElement paramsContainer = element;
            if (element.Name.LocalName.Equals("tool", StringComparison.OrdinalIgnoreCase))
            {
                paramsContainer = element.Element("parameters") ?? element;
                
                // 处理直接子元素作为参数的情况
                if (paramsContainer == element && !element.Elements().Any(e => 
                    e.Name.LocalName.Equals("parameters", StringComparison.OrdinalIgnoreCase)))
                {
                    // 直接使用所有子元素作为参数
                }
            }

            // 提取所有参数
            foreach (var paramElement in paramsContainer.Elements())
            {
                // 跳过parameters元素本身
                if (paramElement.Name.LocalName.Equals("parameters", StringComparison.OrdinalIgnoreCase))
                    continue;
                    
                try
                {
                    ExtractParameter(paramElement, arguments);
                }
                catch (Exception ex)
                {
                    _sLogger?.LogError(ex, $"Failed to extract parameter from element: {paramElement}");
                    continue;
                }
            }

            if (arguments.Count == 0)
            {
                // 尝试从属性中提取参数
                foreach (var attr in element.Attributes())
                {
                    if (!attr.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))
                    {
                        arguments[attr.Name.LocalName] = attr.Value;
                    }
                }
            }

            _sLogger?.LogDebug($"ParseToolElement: Parsed tool '{toolName}' with {arguments.Count} arguments");
            return (toolName, arguments);
        }

        private static void ExtractParameter(XElement paramElement, Dictionary<string, string> arguments)
        {
            try 
            {
                string paramName;
                string paramValue = string.Empty;

                // 处理带name属性的parameter元素
                if (paramElement.Name.LocalName.Equals("parameter", StringComparison.OrdinalIgnoreCase))
                {
                    paramName = paramElement.Attribute("name")?.Value ?? paramElement.Name.LocalName;
                }
                // 处理直接参数元素
                else
                {
                    paramName = paramElement.Name.LocalName;
                }

                // 处理CDATA内容 - 保留原始CDATA标记
                if (paramElement.DescendantNodes().OfType<XCData>().Any())
                {
                    _sLogger?.LogDebug($"ExtractParameter: Found CDATA in parameter {paramName}");
                    var cdataNodes = paramElement.DescendantNodes().OfType<XCData>().ToList();
                    if (cdataNodes.Count == 1)
                    {
                        paramValue = cdataNodes[0].Value;
                        _sLogger?.LogDebug($"ExtractParameter: Single CDATA value length: {paramValue.Length}");
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        foreach (var cdata in cdataNodes)
                        {
                            sb.Append(cdata.Value);
                        }
                        paramValue = sb.ToString();
                        _sLogger?.LogDebug($"ExtractParameter: Combined {cdataNodes.Count} CDATA values, total length: {paramValue.Length}");
                    }
                }
                // 处理嵌套XML结构
                else if (paramElement.HasElements)
                {
                    var sb = new StringBuilder();
                    foreach (var child in paramElement.Elements())
                    {
                        if (child.HasElements)
                        {
                            sb.Append(child.ToString());
                        }
                        else if (child.DescendantNodes().OfType<XCData>().Any())
                        {
                            sb.Append(child.DescendantNodes().OfType<XCData>().First().Value);
                        }
                        else
                        {
                            sb.Append(child.Value);
                        }
                    }
                    paramValue = sb.ToString();
                }
                // 处理普通文本内容
                else
                {
                    paramValue = paramElement.Value.Trim();
                }

                if (!string.IsNullOrWhiteSpace(paramName))
                {
                    _sLogger?.LogDebug($"ExtractParameter: Extracted parameter '{paramName}' with value: {paramValue}");
                    arguments[paramName] = paramValue;
                }
            }
            catch (Exception ex)
            {
                _sLogger?.LogError(ex, $"Error extracting parameter from element: {paramElement}");
                throw;
            }
        }


        private static void ValidateRequiredParameters(string toolName, Dictionary<string, string> arguments)
        {
            var methodParams = ToolRegistry[toolName].Method.GetParameters();
            foreach (var param in methodParams)
            {
                if (!arguments.ContainsKey(param.Name) && 
                    !param.IsOptional && 
                    !param.HasDefaultValue)
                {
                    _sLogger?.LogWarning($"ValidateRequiredParameters: Missing required parameter '{param.Name}' for tool '{toolName}'");
                }
            }
        }

        public static async Task<object> ExecuteRegisteredToolAsync(string toolName, Dictionary<string, string> stringArguments, ToolContext toolContext = null)
        {
            // Clean CDATA markers if present and trim values
            var cleanedArguments = new Dictionary<string, string>();
            foreach (var kvp in stringArguments)
            {
                var value = kvp.Value;
                // Remove CDATA markers if present
                value = Regex.Replace(value, @"<!\[CDATA\[(.*?)\]\]>", "$1").Trim();
                cleanedArguments[kvp.Key] = value;
            }
            stringArguments = cleanedArguments;
            if (!ToolRegistry.TryGetValue(toolName, out var toolInfo))
            {
                throw new ArgumentException($"Tool '{toolName}' not registered.");
            }

            var method = toolInfo.Method;
            var owningType = toolInfo.OwningType;
            var methodParams = method.GetParameters();
            var convertedArgs = new object[methodParams.Length];

            for (int i = 0; i < methodParams.Length; i++)
            {
                var paramInfo = methodParams[i];
                if (paramInfo.ParameterType == typeof(ToolContext))
                {
                    convertedArgs[i] = toolContext;
                    continue;
                }
                
                if (stringArguments.TryGetValue(paramInfo.Name, out var stringValue))
                {
                    convertedArgs[i] = ConvertArgumentValue(stringValue, paramInfo.ParameterType, paramInfo.Name);
                }
                else if (paramInfo.HasDefaultValue)
                {
                    convertedArgs[i] = paramInfo.DefaultValue;
                }
                else if (paramInfo.IsOptional)
                {
                     convertedArgs[i] = Type.Missing; // Or null if appropriate for the type
                }
                else
                {
                    var paramAttr = paramInfo.GetCustomAttribute<McpParameterAttribute>();
                    bool isActuallyRequired = paramAttr?.IsRequired ?? (!paramInfo.IsOptional && !paramInfo.HasDefaultValue && !(paramInfo.ParameterType.IsValueType && Nullable.GetUnderlyingType(paramInfo.ParameterType) == null));
                    if(isActuallyRequired)
                        throw new ArgumentException($"Missing required parameter '{paramInfo.Name}' for tool '{toolName}'.");
                    else // Parameter is implicitly optional (e.g. nullable reference type with no default)
                        convertedArgs[i] = null;
                }
            }

            object instance = null;
            if (!method.IsStatic)
            {
                if (_sServiceProvider != null)
                {
                    instance = _sServiceProvider.GetService(owningType);
                    if (instance == null)
                    {
                        if (owningType.GetConstructor(Type.EmptyTypes) != null)
                            instance = Activator.CreateInstance(owningType);
                    }
                }
                else if (owningType.GetConstructor(Type.EmptyTypes) != null) 
                {
                     instance = Activator.CreateInstance(owningType);
                }
                
                if (instance == null)
                    throw new InvalidOperationException($"Could not create an instance of type '{owningType.FullName}' to execute non-static tool '{toolName}'. Ensure McpToolHelper.EnsureInitialized was called with a valid IServiceProvider and the type is registered in DI or has a parameterless constructor.");
            }

            var result = method.Invoke(instance, convertedArgs);

            if (result is Task taskResult)
            {
                await taskResult; // Await the task if the method is async
                if (taskResult.GetType().IsGenericType) // Check if it's Task<T>
                {
                    return ((dynamic)taskResult).Result; // Get the T result
                }
                return null; // For non-generic Task (void async method)
            }
            return result;
        }

        private static object ConvertArgumentValue(string stringValue, Type targetType, string paramNameForError)
        {
            try
            {
                if (targetType == typeof(string)) return stringValue;
                if (targetType == typeof(int)) return int.Parse(stringValue, CultureInfo.InvariantCulture);
                if (targetType == typeof(long)) return long.Parse(stringValue, CultureInfo.InvariantCulture);
                if (targetType == typeof(float)) return float.Parse(stringValue, CultureInfo.InvariantCulture);
                if (targetType == typeof(double)) return double.Parse(stringValue, CultureInfo.InvariantCulture);
                if (targetType == typeof(bool)) return bool.Parse(stringValue);
                if (targetType == typeof(DateTime)) return DateTime.Parse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                if (targetType.IsEnum) return Enum.Parse(targetType, stringValue, true);

                if (Nullable.GetUnderlyingType(targetType) != null) // For Nullable<T>
                {
                    if (string.IsNullOrEmpty(stringValue)) return null;
                    return ConvertArgumentValue(stringValue, Nullable.GetUnderlyingType(targetType), paramNameForError);
                }
                
                // For complex types, you might try JSON deserialization if stringValue is JSON
                // This is a simplification; robust JSON handling would be more involved.
                 if (!targetType.IsPrimitive && !targetType.IsEnum && targetType != typeof(string) && targetType != typeof(DateTime)) {
                      try {
                        // Use Newtonsoft.Json for deserialization
                        return JsonConvert.DeserializeObject(stringValue, targetType);
                      } catch (Exception jsonEx) {
                         _sLogger?.LogWarning(jsonEx, $"Failed to deserialize '{stringValue}' to type {targetType.Name} using Newtonsoft.Json for parameter {paramNameForError}.");
                         // If deserialization fails, re-throw or handle as appropriate
                         // For now, let the ArgumentException below handle it if conversion isn't possible otherwise.
                     }
                }


                throw new ArgumentException($"Unsupported parameter type '{targetType.Name}' for parameter '{paramNameForError}'. Cannot convert from string.");
            }
            catch (Exception ex)
            {
                _sLogger?.LogError(ex, $"Error converting value '{stringValue}' to type {targetType.Name} for parameter '{paramNameForError}'.");
                throw new ArgumentException($"Error converting value '{stringValue}' for parameter '{paramNameForError}': {ex.Message}", ex);
            }
        }

        private static string GetSimplifiedTypeName(Type type)
        {
            if (type.IsGenericType)
            {
                var genericArgs = string.Join(", ", type.GetGenericArguments().Select(GetSimplifiedTypeName));
                return $"{type.Name.Split('`')[0]}<{genericArgs}>";
            }
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "integer";
            if (type == typeof(long)) return "long";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(DateTime)) return "datetime";
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null) return GetSimplifiedTypeName(underlyingType) + "?"; // e.g. int?
            
            return type.Name;
        }

        public static string ConvertToolResultToString(object toolResultObject) 
        {
            if (toolResultObject == null) {
                return "Tool executed successfully with no return value.";
            } else if (toolResultObject is string s) {
                return s;
            }
            else if (toolResultObject is DuckDuckGoSearchResult ddgResult)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Search Results for \"{ddgResult.Query}\" (Page {ddgResult.CurrentPage}):");
                if (ddgResult.Results == null || !ddgResult.Results.Any())
                {
                    sb.AppendLine("No results found.");
                }
                else
                {
                    int count = 1;
                    foreach (var item in ddgResult.Results)
                    {
                        sb.AppendLine($"{count}. Title: {item.Title}");
                        if (!string.IsNullOrWhiteSpace(item.Description))
                        {
                            sb.AppendLine($"   Snippet: {item.Description.Replace("\n", " ").Trim()}");
                        }
                        sb.AppendLine($"   Source: {item.Url}");
                        count++;
                    }
                    sb.AppendLine($"--- End of {ddgResult.Results.Count} results ---");
                }
                return sb.ToString();
            }
            else {
                try 
                {
                    return JsonConvert.SerializeObject(toolResultObject, Formatting.Indented, 
                        new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
                } 
                catch (Exception ex)
                {
                    _sLogger?.LogError(ex, "Failed to serialize tool result object to JSON. Returning .ToString().");
                    return toolResultObject.ToString();
                }
            }
        }
    }
}

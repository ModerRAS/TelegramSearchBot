using System;
using System.Collections.Concurrent; // Added for ConcurrentDictionary
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
        private static IServiceProvider _serviceProvider; // For DI-based instance resolution
        private static ILogger _logger;

        public static void Initialize(IServiceProvider serviceProvider, ILogger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }
        
        /// <summary>
        /// Scans an assembly for methods marked with McpToolAttribute and registers them.
        /// Also generates the descriptive string for the LLM prompt.
        /// </summary>
        public static string RegisterToolsAndGetPromptString(Assembly assembly)
        {
            ToolRegistry.Clear(); // Clear previous registrations if any
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
                    _logger?.LogWarning($"Duplicate tool name '{toolName}' found or failed to add. Method {method.DeclaringType.FullName}.{method.Name} might be ignored or overwritten by a concurrent call.");
                    // If we want to strictly prevent overwriting and log the original "ignored" message,
                    // we might need a slightly different approach, but ConcurrentDictionary handles the concurrent access safely.
                    // For simplicity and to address the core concurrency bug, TryAdd is sufficient.
                    // If it was already added by another concurrent call, this instance of the tool might not be the one stored.
                    // The duplicate tool name warnings from the user log suggest this is a valid concern.
                    // Let's stick to a pattern closer to the original to ensure the warning logic remains:
                    if (ToolRegistry.ContainsKey(toolName)) // Check first
                    {
                         _logger?.LogWarning($"Duplicate tool name '{toolName}' found. Method {method.DeclaringType.FullName}.{method.Name} will be ignored.");
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
                        _logger?.LogWarning($"Duplicate tool name '{toolName}' encountered for method {method.DeclaringType.FullName}.{method.Name}. This tool registration will be skipped.");
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
        /// Uses the structure previously defined directly in the services.
        /// </summary>
        public static string FormatSystemPrompt(string botName, long chatId, string availableToolsXml)
        {
             if (string.IsNullOrWhiteSpace(botName)) botName = "AI Assistant";
             if (string.IsNullOrWhiteSpace(availableToolsXml)) availableToolsXml = "<!-- No tools are currently available for you to use. -->";

             // Exact prompt structure from OpenAIService/OllamaService
             return $"你的名字是 {botName}，你是一个AI助手。现在时间是：{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")}。当前对话的群聊ID是:{chatId}。\n\n" + 
                    $"你的核心任务是协助用户。为此，你可以调用外部工具。以下是你当前可以使用的工具列表和它们的描述：\n\n" +
                    $"{availableToolsXml}\n\n" + 
                    $"如果你判断需要使用上述列表中的某个工具，你的回复必须严格遵循以下XML格式，并且不包含任何其他文本（不要在XML前后添加任何说明或聊天内容）：\n" +
                    $"<tool_name>\n" + 
                    $"  <parameter1_name>value1</parameter1_name>\n" +
                    $"  <parameter2_name>value2</parameter2_name>\n" +
                    $"  ...\n" +
                    $"</tool_name>\n" +
                    $"或者\n" +
                    $"<tool name=\"tool_name\">\n" +
                    $"  <parameters>\n" +
                    $"    <parameter1_name>value1</parameter1_name>\n" +
                    $"  </parameters>\n" +
                    $"</tool>\n" +
                    $"(请将 'tool_name' 和参数替换为所选工具的实际名称和值。确保XML格式正确无误。)\n\n" +
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
        
        public static bool TryParseToolCalls(string xmlString, out List<(string toolName, Dictionary<string, string> arguments)> parsedToolCalls)
        {
            parsedToolCalls = new List<(string toolName, Dictionary<string, string> arguments)>();
            
            try
            {
                // Trim potential markdown code block fences and whitespace
                xmlString = xmlString.Trim();
                if (xmlString.StartsWith("```xml")) xmlString = xmlString.Substring(6).TrimStart();
                if (xmlString.StartsWith("```")) xmlString = xmlString.Substring(3).TrimStart();
                if (xmlString.EndsWith("```")) xmlString = xmlString.Substring(0, xmlString.Length - 3).TrimEnd();
                xmlString = xmlString.Trim();

                if (!xmlString.StartsWith("<") || !xmlString.EndsWith(">")) return false;

                XDocument xDoc = null;
                try
                {
                    xDoc = XDocument.Parse(xmlString, LoadOptions.None);
                }
                catch (System.Xml.XmlException ex) when (ex.Message != null && ex.Message.ToLowerInvariant().Contains("multiple root elements"))
                {
                    _logger?.LogWarning(ex, $"TryParseToolCall: Multiple root elements detected. Wrapping in <tools_wrapper> and retrying. Original: {xmlString}");
                    try
                    {
                        xDoc = XDocument.Parse($"<tools_wrapper>{xmlString}</tools_wrapper>", LoadOptions.None);
                    }
                    catch (System.Xml.XmlException ex2)
                    {
                        _logger?.LogError(ex2, $"TryParseToolCall: Failed to parse even after wrapping with <tools_wrapper>. Original: {xmlString}");
                        return false;
                    }
                }
                
                if (xDoc == null || xDoc.Root == null) return false;

                XElement rootElement = xDoc.Root;
                if (rootElement == null) return false;

                IEnumerable<XElement> elementsToConsider;
                if (rootElement.Name.LocalName == "tools_wrapper")
                {
                    elementsToConsider = rootElement.Elements();
                }
                else
                {
                    elementsToConsider = new List<XElement> { rootElement };
                }

                foreach (var elementToParse in elementsToConsider)
                {
                    string currentToolName = null;
                    Dictionary<string, string> currentArguments = new Dictionary<string, string>();
                    XElement paramsContainer = null;

                    // Case 1: <tool name="ToolName"><parameters>...</parameters></tool>
                    // or <tool name="ToolName"><parameter name="...">value</parameter></tool>
                    if (elementToParse.Name.LocalName == "tool" && elementToParse.Attribute("name") != null)
                    {
                        var nameAttr = elementToParse.Attribute("name")?.Value;
                        if (nameAttr != null && ToolRegistry.ContainsKey(nameAttr))
                        {
                            currentToolName = nameAttr;
                            paramsContainer = elementToParse.Element("parameters");
                            if (paramsContainer == null)
                            {
                                // Check for direct <parameter> children if no <parameters> container
                                var directParams = elementToParse.Elements("parameter");
                                if (directParams.Any())
                                {
                                    paramsContainer = elementToParse;
                                }
                                else
                                {
                                    _logger?.LogWarning("Tool call format <tool name='{ToolName}'> used, but neither <parameters> nor direct <parameter> elements found.", currentToolName);
                                }
                            }
                        }
                    }
                    // Case 2: <ToolName><arg>val</arg>...</ToolName>
                    else if (ToolRegistry.ContainsKey(elementToParse.Name.LocalName))
                    {
                        currentToolName = elementToParse.Name.LocalName;
                        paramsContainer = elementToParse;
                    }
                    // Case 3: <tool_name><SpecificToolName>...</SpecificToolName></tool_name> (Less common)
                    else if (elementToParse.Name.LocalName == "tool_name" && elementToParse.Elements().Count() == 1)
                    {
                        var innerElement = elementToParse.Elements().First();
                        if (ToolRegistry.ContainsKey(innerElement.Name.LocalName))
                        {
                            currentToolName = innerElement.Name.LocalName;
                            paramsContainer = innerElement;
                        }
                    }

                    if (currentToolName != null)
                    {
                        if (paramsContainer != null)
                        {
                            foreach (var element in paramsContainer.Elements())
                            {
                                if (element.Name.LocalName == "parameter" && element.Attribute("name") != null)
                                {
                                    string key = element.Attribute("name").Value;
                                    string value = element.Value;
                                    if (!string.IsNullOrEmpty(key)) currentArguments[key] = value;
                                }
                                else
                                {
                                    currentArguments[element.Name.LocalName] = element.Value;
                                }
                            }
                        }
                        parsedToolCalls.Add((currentToolName, currentArguments));
                    }
                    else
                    {
                         _logger?.LogWarning($"TryParseToolCalls: Skipped unrecognized element '{elementToParse.Name.LocalName}' during multi-tool parse attempt. Element: {elementToParse}");
                    }
                }
                return parsedToolCalls.Any();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error parsing tool call XML: {xmlString}");
                return false;
            }
        }

        public static async Task<object> ExecuteRegisteredToolAsync(string toolName, Dictionary<string, string> stringArguments)
        {
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
                if (_serviceProvider != null)
                {
                    instance = _serviceProvider.GetService(owningType);
                    if (instance == null)
                    {
                        // Try to activate if not in DI and has parameterless constructor
                        if (owningType.GetConstructor(Type.EmptyTypes) != null)
                            instance = Activator.CreateInstance(owningType);
                    }
                }
                else if (owningType.GetConstructor(Type.EmptyTypes) != null)
                {
                     instance = Activator.CreateInstance(owningType);
                }
                
                if (instance == null)
                    throw new InvalidOperationException($"Could not create an instance of type '{owningType.FullName}' to execute non-static tool '{toolName}'. Register it in DI or ensure it has a parameterless constructor.");
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
                         _logger?.LogWarning(jsonEx, $"Failed to deserialize '{stringValue}' to type {targetType.Name} using Newtonsoft.Json for parameter {paramNameForError}.");
                         // If deserialization fails, re-throw or handle as appropriate
                         // For now, let the ArgumentException below handle it if conversion isn't possible otherwise.
                     }
                }


                throw new ArgumentException($"Unsupported parameter type '{targetType.Name}' for parameter '{paramNameForError}'. Cannot convert from string.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error converting value '{stringValue}' to type {targetType.Name} for parameter '{paramNameForError}'.");
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
                    _logger?.LogError(ex, "Failed to serialize tool result object to JSON. Returning .ToString().");
                    return toolResultObject.ToString();
                }
            }
        }
    }
}

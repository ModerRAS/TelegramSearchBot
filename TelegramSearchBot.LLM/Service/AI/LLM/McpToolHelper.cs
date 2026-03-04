using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// Manages built-in tools (decorated with BuiltInToolAttribute or McpToolAttribute) and
    /// external MCP tools from connected MCP servers.
    /// </summary>
    public static class McpToolHelper {
        private static readonly ConcurrentDictionary<string, (MethodInfo Method, Type OwningType)> ToolRegistry = new ConcurrentDictionary<string, (MethodInfo, Type)>();
        private static readonly ConcurrentDictionary<string, ExternalToolInfo> ExternalToolRegistry = new ConcurrentDictionary<string, ExternalToolInfo>();
        private static Func<string, string, Dictionary<string, string>, Task<string>> _externalToolExecutor;
        private static IServiceProvider _sServiceProvider;
        private static ILogger _sLogger;
        private static string _sCachedToolsXml;
        private static string _sCachedExternalToolsXml;
        private static bool _sIsInitialized = false;
        private static readonly object _initializationLock = new object();

        /// <summary>
        /// Information about an external tool from an MCP server.
        /// </summary>
        public class ExternalToolInfo {
            public string ServerName { get; set; }
            public string ToolName { get; set; }
            public string Description { get; set; }
            public List<ExternalToolParameter> Parameters { get; set; } = new();
        }

        public class ExternalToolParameter {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Description { get; set; }
            public bool Required { get; set; }
        }

        private static void Initialize(IServiceProvider serviceProvider, ILogger logger) {
            _sServiceProvider = serviceProvider;
            _sLogger = logger;
        }

        /// <summary>
        /// Ensures that McpToolHelper is initialized, tools are registered, and the prompt string is cached.
        /// Scans both the main assembly and the LLM assembly for tool attributes.
        /// This method should be called once at application startup.
        /// </summary>
        public static void EnsureInitialized(Assembly mainAssembly, Assembly llmAssembly, IServiceProvider serviceProvider, ILogger logger) {
            if (_sIsInitialized) {
                return;
            }

            lock (_initializationLock) {
                if (_sIsInitialized) {
                    return;
                }

                Initialize(serviceProvider, logger);

                _sLogger?.LogInformation("McpToolHelper.EnsureInitialized: Starting tool registration...");
                var assemblies = new List<Assembly> { mainAssembly };
                if (llmAssembly != null && llmAssembly != mainAssembly) {
                    assemblies.Add(llmAssembly);
                }
                _sCachedToolsXml = RegisterToolsAndGetPromptString(assemblies);
                if (string.IsNullOrWhiteSpace(_sCachedToolsXml)) {
                    _sCachedToolsXml = "<!-- No tools are currently available. -->";
                    _sLogger?.LogWarning("McpToolHelper.EnsureInitialized: No tools found or registered. Prompt will indicate no tools available.");
                } else {
                    _sLogger?.LogInformation("McpToolHelper.EnsureInitialized: Tools registered and XML prompt cached successfully.");
                }

                _sIsInitialized = true;
            }
        }

        /// <summary>
        /// Backward-compatible overload that scans a single assembly.
        /// </summary>
        public static void EnsureInitialized(Assembly assembly, IServiceProvider serviceProvider, ILogger logger) {
            EnsureInitialized(assembly, null, serviceProvider, logger);
        }

        /// <summary>
        /// Scans an assembly for methods marked with BuiltInToolAttribute or McpToolAttribute and registers them.
        /// Also generates the descriptive string for the LLM prompt.
        /// This method is called by EnsureInitialized.
        /// </summary>
        private static string RegisterToolsAndGetPromptString(List<Assembly> assemblies) {
            ToolRegistry.Clear();

            var loggerForRegistration = _sLogger;

            // Scan all assemblies for both BuiltInToolAttribute and (deprecated) McpToolAttribute
            var methods = assemblies
                                  .SelectMany(a => a.GetTypes())
                                  .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                                  .Where(m => m.GetCustomAttribute<BuiltInToolAttribute>() != null ||
                                              m.GetCustomAttribute<McpToolAttribute>() != null)
                                  .ToList();

            var sb = new StringBuilder();
            foreach (var method in methods) {
                // Prefer BuiltInToolAttribute, fall back to McpToolAttribute
                var builtInAttr = method.GetCustomAttribute<BuiltInToolAttribute>();
                var mcpAttr = method.GetCustomAttribute<McpToolAttribute>();
                var description = builtInAttr?.Description ?? mcpAttr?.Description;
                var nameOverride = builtInAttr?.Name ?? mcpAttr?.Name;
                if (description == null) continue;

                var toolName = string.IsNullOrWhiteSpace(nameOverride) ? method.Name : nameOverride;
                toolName = toolName.Split('`')[0]; // Sanitize

                if (!ToolRegistry.TryAdd(toolName, (method, method.DeclaringType))) {
                    loggerForRegistration?.LogWarning($"Duplicate tool name '{toolName}' found. Method {method.DeclaringType.FullName}.{method.Name} will be ignored.");
                    continue;
                }

                sb.AppendLine($"- <tool name=\"{toolName}\">");
                sb.AppendLine($"    <description>{description}</description>");
                sb.AppendLine($"    <parameters>");
                foreach (var param in method.GetParameters()) {
                    // Check both attribute types
                    var builtInParamAttr = param.GetCustomAttribute<BuiltInParameterAttribute>();
                    var mcpParamAttr = param.GetCustomAttribute<McpParameterAttribute>();
                    var paramDescription = builtInParamAttr?.Description ?? mcpParamAttr?.Description ?? $"Parameter '{param.Name}'";
                    var paramIsRequired = builtInParamAttr?.IsRequired ?? mcpParamAttr?.IsRequired ?? ( !param.IsOptional && !param.HasDefaultValue && !( param.ParameterType.IsValueType && Nullable.GetUnderlyingType(param.ParameterType) == null ) );
                    var paramType = GetSimplifiedTypeName(param.ParameterType);
                    sb.AppendLine($"        <parameter name=\"{param.Name}\" type=\"{paramType}\" required=\"{paramIsRequired.ToString().ToLower()}\">{paramDescription}</parameter>");
                }
                sb.AppendLine($"    </parameters>");
                sb.AppendLine($"  </tool>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Generates OpenAI-compatible ChatTool definitions for all registered tools (built-in and external).
        /// Used for native function/tool calling API instead of XML prompt-based tool calling.
        /// </summary>
        public static List<OpenAI.Chat.ChatTool> GetNativeToolDefinitions() {
            var tools = new List<OpenAI.Chat.ChatTool>();

            // Built-in tools
            foreach (var (toolName, toolInfo) in ToolRegistry) {
                var method = toolInfo.Method;
                var builtInAttr = method.GetCustomAttribute<BuiltInToolAttribute>();
                var mcpAttr = method.GetCustomAttribute<McpToolAttribute>();
                var description = builtInAttr?.Description ?? mcpAttr?.Description ?? "";

                var properties = new Dictionary<string, object>();
                var required = new List<string>();

                foreach (var param in method.GetParameters()) {
                    if (param.ParameterType == typeof(ToolContext)) continue;

                    var builtInParamAttr = param.GetCustomAttribute<BuiltInParameterAttribute>();
                    var mcpParamAttr = param.GetCustomAttribute<McpParameterAttribute>();
                    var paramDescription = builtInParamAttr?.Description ?? mcpParamAttr?.Description ?? $"Parameter '{param.Name}'";
                    var paramIsRequired = builtInParamAttr?.IsRequired ?? mcpParamAttr?.IsRequired ?? (!param.IsOptional && !param.HasDefaultValue);
                    var paramType = MapToJsonSchemaType(param.ParameterType);

                    properties[param.Name] = new Dictionary<string, object> {
                        { "type", paramType },
                        { "description", paramDescription }
                    };

                    if (paramIsRequired) {
                        required.Add(param.Name);
                    }
                }

                var parametersSchema = new Dictionary<string, object> {
                    { "type", "object" },
                    { "properties", properties },
                    { "required", required }
                };

                var parametersJson = JsonConvert.SerializeObject(parametersSchema);
                tools.Add(OpenAI.Chat.ChatTool.CreateFunctionTool(toolName, description, BinaryData.FromString(parametersJson)));
            }

            // External MCP tools
            foreach (var (qualifiedName, toolInfo) in ExternalToolRegistry) {
                var properties = new Dictionary<string, object>();
                var required = new List<string>();

                foreach (var param in toolInfo.Parameters) {
                    properties[param.Name] = new Dictionary<string, object> {
                        { "type", MapExternalTypeToJsonSchema(param.Type) },
                        { "description", param.Description }
                    };

                    if (param.Required) {
                        required.Add(param.Name);
                    }
                }

                var parametersSchema = new Dictionary<string, object> {
                    { "type", "object" },
                    { "properties", properties },
                    { "required", required }
                };

                var description = $"[MCP Server: {toolInfo.ServerName}] {toolInfo.Description}";
                var parametersJson = JsonConvert.SerializeObject(parametersSchema);
                tools.Add(OpenAI.Chat.ChatTool.CreateFunctionTool(qualifiedName, description, BinaryData.FromString(parametersJson)));
            }

            return tools;
        }

        private static string MapToJsonSchemaType(Type type) {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) type = underlying;

            if (type == typeof(string)) return "string";
            if (type == typeof(int) || type == typeof(long)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type == typeof(bool)) return "boolean";
            return "string";
        }

        private static string MapExternalTypeToJsonSchema(string type) {
            return type?.ToLower() switch {
                "integer" or "int" or "long" => "integer",
                "number" or "float" or "double" => "number",
                "boolean" or "bool" => "boolean",
                "array" => "array",
                "object" => "object",
                _ => "string"
            };
        }

        /// <summary>
        /// Formats the standard system prompt incorporating tool descriptions and usage instructions.
        /// Includes both built-in tools and external MCP tools, and shell environment information.
        /// </summary>
        public static string FormatSystemPrompt(string botName, long chatId) {
            if (!_sIsInitialized) {
                _sLogger?.LogCritical("McpToolHelper.FormatSystemPrompt called before EnsureInitialized. Tool descriptions will be missing or incorrect.");
            }

            if (string.IsNullOrWhiteSpace(botName)) botName = "AI Assistant";
            string builtInToolsXml = _sCachedToolsXml ?? "<!-- No built-in tools available. -->";
            string externalToolsXml = _sCachedExternalToolsXml ?? "";

            var toolsSectionBuilder = new StringBuilder();
            toolsSectionBuilder.AppendLine("== 内置工具 (Built-in Tools) ==");
            toolsSectionBuilder.AppendLine(builtInToolsXml);

            if (!string.IsNullOrWhiteSpace(externalToolsXml)) {
                toolsSectionBuilder.AppendLine();
                toolsSectionBuilder.AppendLine("== 外部MCP工具 (External MCP Tools) ==");
                toolsSectionBuilder.AppendLine(externalToolsXml);
            }

            string toolsXmlToUse = toolsSectionBuilder.ToString();

            // Shell environment description
            string shellEnvInfo = TelegramSearchBot.Service.Tools.BashToolService.GetShellEnvironmentDescription();

            return $"你的名字是 {botName}，你是一个AI助手。现在时间是：{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")}。当前对话的群聊ID是:{chatId}。\n\n" +
                   $"== 运行环境信息 ==\n{shellEnvInfo}\n\n" +
                   $"你的核心任务是协助用户。为此，你可以调用工具。以下是你当前可以使用的工具列表和它们的描述：\n\n" +
                   $"{toolsXmlToUse}\n\n" +
                   $"当你需要调用工具时，必须严格遵循以下规则：\n\n" +
                   "1. 工具调用必须使用XML格式，且必须是回复的唯一内容\n" +
                   "2. 每次只能调用一个工具，多个工具必须分多次调用\n" +
                   "3. 工具调用格式示例：\n" +
                   "```xml\n" +
                   "<tool name=\"tool_name\">\n" +
                   "  <parameters>\n" +
                   "    <parameter1>value1</parameter1>\n" +
                   "    <parameter2>value2</parameter2>\n" +
                   "  </parameters>\n" +
                   "</tool>\n" +
                   "```\n\n" +
                   "4. 关键要求：\n" +
                   "- 确保工具名称和参数名称完全匹配\n" +
                   "- 参数值必须正确格式化（字符串加引号，数字不加）\n" +
                   "- 不要包含任何额外的解释或聊天内容\n" +
                   "- 如果参数值包含特殊字符，确保正确转义\n" +
                   "- 严格禁止在单个消息中调用多个工具\n\n" +
                   "5. 工具调用流程：\n" +
                   "- 每次只能使用一个工具\n" +
                   "- 等待工具执行结果后再决定下一步操作\n" +
                   "- 如果需要多个工具，必须分多次调用\n" +
                   "- 工具调用之间必须等待用户确认执行结果\n\n" +
                   "重要提示：如果你调用一个工具（特别是搜索类工具）后没有找到你需要的信息，或者结果不理想，你可以尝试以下操作：\n" +
                   "1. 修改你的查询参数（例如，使用更宽泛或更具体的关键词，尝试不同的搜索选项等），然后再次调用同一个工具。\n" +
                   "2. 如果多次尝试仍不理想，或者你认为其他工具可能更合适，可以尝试调用其他可用工具。\n" +
                   "3. 在进行多次尝试时，建议在思考过程中记录并调整你的策略。\n" +
                   "如果你认为已经获得了足够的信息，或者不需要再使用工具，请继续下一步。\n\n" +
                   "关于MCP工具管理：你可以使用以下内置工具来管理外部MCP工具服务器：\n" +
                   "- ListMcpServers: 列出所有已配置的MCP服务器，包括连接状态和可用工具\n" +
                   "- AddMcpServer: 添加新的MCP服务器。参数：name(唯一名称), command(启动命令如npx), args(命令参数如'-y @playwright/mcp'), env(可选环境变量如'KEY=value')\n" +
                   "- RemoveMcpServer: 通过名称删除MCP服务器\n" +
                   "- RestartMcpServers: 重启所有已启用的MCP服务器\n" +
                   "常见MCP服务器：\n" +
                   "- Playwright浏览器: AddMcpServer(name='playwright', command='npx', args='-y @playwright/mcp')\n" +
                   "- 文件系统: AddMcpServer(name='filesystem', command='npx', args='-y @modelcontextprotocol/server-filesystem /path/to/directory')\n" +
                   "- GitHub: AddMcpServer(name='github', command='npx', args='-y @modelcontextprotocol/server-github', env='GITHUB_TOKEN=xxx')\n" +
                   "安装前建议用 ExecuteCommand 工具检查npm/npx是否已安装。\n\n" +
                   "引用说明：当你使用工具（特别是搜索工具）获取信息并在回答中使用了这些信息时，如果工具结果提供了来源(Source)或链接(URL)，请务必在你的回答中清晰地注明来源。你可以使用Markdown链接格式，例如 `[来源标题](URL)`，或者在回答末尾列出引用来源列表。确保用户可以追溯信息的原始出处。\n\n" +
                   $"在决定是否使用工具时，请仔细分析用户的请求。如果不需要工具，或者工具执行完毕后，请直接以自然语言回复用户。\n" +
                   $"当你直接回复时，请直接输出内容，不要模仿历史消息的格式。";
        }

        /// <summary>
        /// Formats a simplified system prompt for use with native API tool calling.
        /// No XML tool instructions are needed since tools are passed via the API.
        /// </summary>
        public static string FormatSystemPromptForNativeToolCalling(string botName, long chatId) {
            if (string.IsNullOrWhiteSpace(botName)) botName = "AI Assistant";

            // Shell environment description
            string shellEnvInfo = TelegramSearchBot.Service.Tools.BashToolService.GetShellEnvironmentDescription();

            return $"你的名字是 {botName}，你是一个AI助手。现在时间是：{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")}。当前对话的群聊ID是:{chatId}。\n\n" +
                   $"== 运行环境信息 ==\n{shellEnvInfo}\n\n" +
                   "你的核心任务是协助用户。你可以使用提供的工具来帮助用户完成任务。\n\n" +
                   "工具使用指南：\n" +
                   "- 仔细分析用户请求，判断是否需要调用工具\n" +
                   "- 如果搜索类工具没有找到满意结果，可以修改关键词重试或使用其他工具\n" +
                   "- 工具执行完毕后，请基于结果以自然语言回复用户\n\n" +
                   "关于MCP工具管理：\n" +
                   "- ListMcpServers: 列出所有已配置的MCP服务器及其状态\n" +
                   "- AddMcpServer: 添加新的MCP服务器（需要name、command、args参数）。常见的MCP服务器：\n" +
                   "  * Playwright浏览器: command='npx', args='-y @playwright/mcp'\n" +
                   "  * 文件系统: command='npx', args='-y @modelcontextprotocol/server-filesystem /path'\n" +
                   "  * GitHub: command='npx', args='-y @modelcontextprotocol/server-github', env='GITHUB_TOKEN=xxx'\n" +
                   "  * Brave搜索: command='npx', args='-y @modelcontextprotocol/server-brave-search', env='BRAVE_API_KEY=xxx'\n" +
                   "- RemoveMcpServer: 删除指定的MCP服务器\n" +
                   "- RestartMcpServers: 重启所有MCP服务器\n" +
                   "在安装MCP服务器之前，可以先用 ExecuteCommand 检查环境（如npm/npx是否已安装）。\n\n" +
                   "引用说明：使用搜索工具获取信息时，如果结果提供了来源或链接，请在回答中注明来源，使用Markdown链接格式如 `[来源标题](URL)`。\n\n" +
                   "当你直接回复时，请直接输出内容，不要模仿历史消息的格式。";
        }

        /// <summary>
        /// Removes thinking tags and trims whitespace from raw LLM response.
        /// </summary>
        /// <param name="rawResponse">The raw response string from the LLM.</param>
        /// <returns>The cleaned response string.</returns>
        public static string CleanLlmResponse(string rawResponse) {
            if (string.IsNullOrEmpty(rawResponse)) {
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

        public static bool TryParseToolCalls(string input, out List<(string toolName, Dictionary<string, string> arguments)> parsedToolCalls) {
            parsedToolCalls = new List<(string toolName, Dictionary<string, string> arguments)>();

            if (string.IsNullOrWhiteSpace(input)) {
                _sLogger?.LogDebug("TryParseToolCalls: Input is null or empty");
                return false;
            }

            try {
                string processedInput = input.Trim();
                _sLogger?.LogDebug($"Original input: {processedInput}");

                // Handle Markdown code blocks first
                if (processedInput.Contains("```xml") || processedInput.Contains("```")) {
                    var codeBlocks = new List<string>();
                    var matches = Regex.Matches(processedInput,
                        @"```(?:xml)?\s*(<tool[\s\S]*?<\/tool>|<[\w]+>[\s\S]*?<\/[\w]+>)\s*```",
                        RegexOptions.Multiline);

                    foreach (Match m in matches) {
                        var content = m.Groups[1].Value.Trim();
                        codeBlocks.Add(content);
                    }

                    if (codeBlocks.Count > 0) {
                        // Combine all code block content for subsequent parsing
                        processedInput = string.Join("\n", codeBlocks);
                    } else if (processedInput.Contains("<tool")) {
                        // If it contains <tool> but no markdown, just trim
                        processedInput = processedInput.Trim();
                    } else {
                        _sLogger?.LogWarning("No valid XML content found in code blocks or as raw XML");
                        return false;
                    }
                }
                // If no markdown, check if it looks like raw XML
                else if (processedInput.Contains("<") && processedInput.Contains(">")) {
                    // Keep processedInput as is for raw XML parsing attempt
                } else {
                    _sLogger?.LogDebug("TryParseToolCalls: Input does not appear to be XML or markdown code block.");
                    return false;
                }

                // --- Improved Parsing Logic ---
                // First try to parse the input as a whole document
                try {
                    XDocument doc = XDocument.Parse(processedInput);
                    // Check if there's a wrapper element containing tool elements
                    if (doc.Root.Elements().Any(e =>
                        e.Name.LocalName.Equals("tool", StringComparison.OrdinalIgnoreCase) ||
                        ToolRegistry.ContainsKey(e.Name.LocalName))) {
                        // Process each tool element inside the wrapper
                        foreach (var toolElement in doc.Root.Elements()) {
                            var toolCall = ParseToolElement(toolElement);
                            if (toolCall.toolName != null) {
                                parsedToolCalls.Add(toolCall);
                            }
                        }
                        return parsedToolCalls.Count > 0;
                    }
                } catch (System.Xml.XmlException) {
                    // Fall through to regex parsing if XML parsing fails
                }

                // Fallback to regex parsing for individual tool blocks
                var toolBlockMatches = Regex.Matches(processedInput, @"<(tool\b[^>]*|[\w]+)(?:\s[^>]*)?>[\s\S]*?<\/\1>", RegexOptions.IgnoreCase);
                _sLogger?.LogInformation($"TryParseToolCalls: Found {toolBlockMatches.Count} tool elements in input: {processedInput}");
                _sLogger?.LogInformation($"TryParseToolCalls: Raw regex matches - Count: {toolBlockMatches.Count}");
                _sLogger?.LogInformation($"TryParseToolCalls: Using regex pattern: {@"<(tool\b[^>]*|[\w]+)(?:\s[^>]*)?>[\s\S]*?<\/\1>"}");
                for (int i = 0; i < toolBlockMatches.Count; i++) {
                    _sLogger?.LogInformation($"Match {i}: {toolBlockMatches[i].Value}");
                }

                _sLogger?.LogDebug($"TryParseToolCalls: Found {toolBlockMatches.Count} potential tool blocks using regex.");

                if (toolBlockMatches.Count == 0) {
                    _sLogger?.LogDebug("TryParseToolCalls: No potential tool blocks found after initial processing.");
                    return false;
                }

                foreach (Match match in toolBlockMatches) {
                    string blockXml = match.Value;
                    _sLogger?.LogDebug($"TryParseToolCalls: Processing block: {blockXml}");
                    try {
                        // Attempt to parse the individual block XML
                        XDocument blockDoc = XDocument.Parse(blockXml, LoadOptions.PreserveWhitespace);
                        XElement blockRoot = blockDoc.Root;

                        // Use the existing ParseToolElement logic to extract tool name and arguments
                        var toolCall = ParseToolElement(blockRoot);
                        if (toolCall.toolName != null) {
                            parsedToolCalls.Add(toolCall);
                            _sLogger?.LogDebug($"TryParseToolCalls: Successfully parsed tool '{toolCall.toolName}'. Current count: {parsedToolCalls.Count}");
                        } else {
                            _sLogger?.LogDebug($"TryParseToolCalls: ParseToolElement returned null toolName for block: {blockXml}");
                        }
                    } catch (System.Xml.XmlException ex) {
                        _sLogger?.LogError(ex, $"TryParseToolCalls: Failed to parse individual tool block XML: {blockXml}");
                        // Continue to the next block
                    } catch (Exception ex) {
                        _sLogger?.LogError(ex, $"TryParseToolCalls: Unexpected error processing tool block: {blockXml}");
                        // Continue to the next block
                    }
                }

                _sLogger?.LogDebug($"TryParseToolCalls: Final parsedToolCalls count: {parsedToolCalls.Count}");
                return parsedToolCalls.Count > 0; // Return true if any tools were parsed
            } catch (Exception ex) {
                _sLogger?.LogError(ex, "TryParseToolCalls: Unexpected error parsing tool calls");
                return false;
            }
        }

        private static (string toolName, Dictionary<string, string> arguments) ParseToolElement(XElement element) {
            string toolName = null;
            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 确定工具名称 - 更灵活的匹配逻辑
            if (element.Name.LocalName.Equals("tool", StringComparison.OrdinalIgnoreCase)) {
                toolName = element.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(toolName)) {
                    _sLogger?.LogWarning("ParseToolElement: Tool element has no name attribute");
                    return (null, null);
                }
            } else {
                // 尝试匹配注册的工具名称 (built-in and external)
                toolName = ToolRegistry.Keys.FirstOrDefault(k =>
                    k.Equals(element.Name.LocalName, StringComparison.OrdinalIgnoreCase));
                if (toolName == null) {
                    toolName = ExternalToolRegistry.Keys.FirstOrDefault(k =>
                        k.Equals(element.Name.LocalName, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (toolName == null || (!ToolRegistry.ContainsKey(toolName) && !ExternalToolRegistry.ContainsKey(toolName))) {
                _sLogger?.LogWarning($"ParseToolElement: Unregistered tool '{element.Name.LocalName}'");
                return (null, null);
            }

            // 处理CDATA内容 - 保留原始CDATA标记
            foreach (var cdata in element.DescendantNodes().OfType<XCData>()) {
                var parent = cdata.Parent;
                if (parent != null) {
                    arguments[parent.Name.LocalName] = cdata.Value;
                }
            }

            // 获取参数容器 - 更灵活的查找逻辑
            XElement paramsContainer = element;
            if (element.Name.LocalName.Equals("tool", StringComparison.OrdinalIgnoreCase)) {
                paramsContainer = element.Element("parameters") ?? element;

                // 处理直接子元素作为参数的情况
                if (paramsContainer == element && !element.Elements().Any(e =>
                    e.Name.LocalName.Equals("parameters", StringComparison.OrdinalIgnoreCase))) {
                    // 直接使用所有子元素作为参数
                }
            }

            // 提取所有参数
            foreach (var paramElement in paramsContainer.Elements()) {
                // 跳过parameters元素本身
                if (paramElement.Name.LocalName.Equals("parameters", StringComparison.OrdinalIgnoreCase))
                    continue;

                try {
                    ExtractParameter(paramElement, arguments);
                } catch (Exception ex) {
                    _sLogger?.LogError(ex, $"Failed to extract parameter from element: {paramElement}");
                    continue;
                }
            }

            if (arguments.Count == 0) {
                // 尝试从属性中提取参数
                foreach (var attr in element.Attributes()) {
                    if (!attr.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase)) {
                        arguments[attr.Name.LocalName] = attr.Value;
                    }
                }
            }

            _sLogger?.LogDebug($"ParseToolElement: Parsed tool '{toolName}' with {arguments.Count} arguments");
            return (toolName, arguments);
        }

        private static void ExtractParameter(XElement paramElement, Dictionary<string, string> arguments) {
            try {
                string paramName;
                string paramValue = string.Empty;

                // 处理带name属性的parameter元素
                if (paramElement.Name.LocalName.Equals("parameter", StringComparison.OrdinalIgnoreCase)) {
                    paramName = paramElement.Attribute("name")?.Value ?? paramElement.Name.LocalName;
                }
                // 处理直接参数元素
                else {
                    paramName = paramElement.Name.LocalName;
                }

                // 处理CDATA内容 - 保留原始CDATA标记
                if (paramElement.DescendantNodes().OfType<XCData>().Any()) {
                    _sLogger?.LogDebug($"ExtractParameter: Found CDATA in parameter {paramName}");
                    var cdataNodes = paramElement.DescendantNodes().OfType<XCData>().ToList();
                    if (cdataNodes.Count == 1) {
                        paramValue = cdataNodes[0].Value;
                        _sLogger?.LogDebug($"ExtractParameter: Single CDATA value length: {paramValue.Length}");
                    } else {
                        var sb = new StringBuilder();
                        foreach (var cdata in cdataNodes) {
                            sb.Append(cdata.Value);
                        }
                        paramValue = sb.ToString();
                        _sLogger?.LogDebug($"ExtractParameter: Combined {cdataNodes.Count} CDATA values, total length: {paramValue.Length}");
                    }
                }
                // 处理嵌套XML结构
                else if (paramElement.HasElements) {
                    var sb = new StringBuilder();
                    foreach (var child in paramElement.Elements()) {
                        if (child.HasElements) {
                            sb.Append(child.ToString());
                        } else if (child.DescendantNodes().OfType<XCData>().Any()) {
                            sb.Append(child.DescendantNodes().OfType<XCData>().First().Value);
                        } else {
                            sb.Append(child.Value);
                        }
                    }
                    paramValue = sb.ToString();
                }
                // 处理普通文本内容
                else {
                    paramValue = paramElement.Value.Trim();
                }

                if (!string.IsNullOrWhiteSpace(paramName)) {
                    _sLogger?.LogDebug($"ExtractParameter: Extracted parameter '{paramName}' with value: {paramValue}");
                    arguments[paramName] = paramValue;
                }
            } catch (Exception ex) {
                _sLogger?.LogError(ex, $"Error extracting parameter from element: {paramElement}");
                throw;
            }
        }


        private static void ValidateRequiredParameters(string toolName, Dictionary<string, string> arguments) {
            var methodParams = ToolRegistry[toolName].Method.GetParameters();
            foreach (var param in methodParams) {
                if (!arguments.ContainsKey(param.Name) &&
                    !param.IsOptional &&
                    !param.HasDefaultValue) {
                    _sLogger?.LogWarning($"ValidateRequiredParameters: Missing required parameter '{param.Name}' for tool '{toolName}'");
                }
            }
        }

        public static async Task<object> ExecuteRegisteredToolAsync(string toolName, Dictionary<string, string> stringArguments, ToolContext toolContext = null) {
            // Clean CDATA markers if present and trim values
            var cleanedArguments = new Dictionary<string, string>();
            foreach (var kvp in stringArguments) {
                var value = kvp.Value;
                value = Regex.Replace(value, @"<!\[CDATA\[(.*?)\]\]>", "$1").Trim();
                cleanedArguments[kvp.Key] = value;
            }
            stringArguments = cleanedArguments;

            // Check if this is an external MCP tool
            if (ExternalToolRegistry.TryGetValue(toolName, out var externalTool)) {
                return await ExecuteExternalToolAsync(toolName, externalTool, stringArguments);
            }

            if (!ToolRegistry.TryGetValue(toolName, out var toolInfo)) {
                throw new ArgumentException($"Tool '{toolName}' not registered.");
            }

            var method = toolInfo.Method;
            var owningType = toolInfo.OwningType;
            var methodParams = method.GetParameters();
            var convertedArgs = new object[methodParams.Length];

            for (int i = 0; i < methodParams.Length; i++) {
                var paramInfo = methodParams[i];
                if (paramInfo.ParameterType == typeof(ToolContext)) {
                    convertedArgs[i] = toolContext;
                    continue;
                }

                if (stringArguments.TryGetValue(paramInfo.Name, out var stringValue)) {
                    convertedArgs[i] = ConvertArgumentValue(stringValue, paramInfo.ParameterType, paramInfo.Name);
                } else if (paramInfo.HasDefaultValue) {
                    convertedArgs[i] = paramInfo.DefaultValue;
                } else if (paramInfo.IsOptional) {
                    convertedArgs[i] = Type.Missing;
                } else {
                    // Check both attribute types
                    var builtInParamAttr = paramInfo.GetCustomAttribute<BuiltInParameterAttribute>();
                    var mcpParamAttr = paramInfo.GetCustomAttribute<McpParameterAttribute>();
                    bool isActuallyRequired = builtInParamAttr?.IsRequired ?? mcpParamAttr?.IsRequired ?? ( !paramInfo.IsOptional && !paramInfo.HasDefaultValue && !( paramInfo.ParameterType.IsValueType && Nullable.GetUnderlyingType(paramInfo.ParameterType) == null ) );
                    if (isActuallyRequired)
                        throw new ArgumentException($"Missing required parameter '{paramInfo.Name}' for tool '{toolName}'.");
                    else
                        convertedArgs[i] = null;
                }
            }

            object instance = null;
            if (!method.IsStatic) {
                if (_sServiceProvider != null) {
                    instance = _sServiceProvider.GetService(owningType);
                    if (instance == null) {
                        if (owningType.GetConstructor(Type.EmptyTypes) != null)
                            instance = Activator.CreateInstance(owningType);
                    }
                } else if (owningType.GetConstructor(Type.EmptyTypes) != null) {
                    instance = Activator.CreateInstance(owningType);
                }

                if (instance == null)
                    throw new InvalidOperationException($"Could not create an instance of type '{owningType.FullName}' to execute non-static tool '{toolName}'. Ensure McpToolHelper.EnsureInitialized was called with a valid IServiceProvider and the type is registered in DI or has a parameterless constructor.");
            }

            var result = method.Invoke(instance, convertedArgs);

            if (result is Task taskResult) {
                await taskResult;
                if (taskResult.GetType().IsGenericType)
                {
                    return ( ( dynamic ) taskResult ).Result;
                }
                return null;
            }
            return result;
        }

        private static object ConvertArgumentValue(string stringValue, Type targetType, string paramNameForError) {
            try {
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
            } catch (Exception ex) {
                _sLogger?.LogError(ex, $"Error converting value '{stringValue}' to type {targetType.Name} for parameter '{paramNameForError}'.");
                throw new ArgumentException($"Error converting value '{stringValue}' for parameter '{paramNameForError}': {ex.Message}", ex);
            }
        }

        private static string GetSimplifiedTypeName(Type type) {
            if (type.IsGenericType) {
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

        public static string ConvertToolResultToString(object toolResultObject) {
            if (toolResultObject == null) {
                return "Tool executed successfully with no return value.";
            } else if (toolResultObject is string s) {
                return s;
            } else if (toolResultObject is BraveSearchResult braveResult) {
                var sb = new StringBuilder();
                sb.AppendLine("Search Results:");
                if (braveResult.Web?.Results == null || !braveResult.Web.Results.Any()) {
                    sb.AppendLine("No results found.");
                } else {
                    int count = 1;
                    foreach (var item in braveResult.Web.Results.Take(5)) // Limit to 5 results
                    {
                        sb.AppendLine($"{count}. Title: {item.Title}");
                        if (!string.IsNullOrWhiteSpace(item.Description)) {
                            sb.AppendLine($"   Snippet: {item.Description.Replace("\n", " ").Trim()}");
                        }
                        sb.AppendLine($"   Source: {item.Url}");
                        count++;
                    }
                    sb.AppendLine($"--- End of {Math.Min(braveResult.Web.Results.Count, 5)} results ---");
                }
                return sb.ToString();
            } else {
                try {
                    return JsonConvert.SerializeObject(toolResultObject, Formatting.Indented,
                        new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
                } catch (Exception ex) {
                    _sLogger?.LogError(ex, "Failed to serialize tool result object to JSON. Returning .ToString().");
                    return toolResultObject.ToString();
                }
            }
        }
        /// <summary>
        /// Register external MCP tools from connected MCP servers.
        /// These tools are added to the system prompt and routed to the MCP server for execution.
        /// </summary>
        /// <param name="tools">List of (serverName, tool) tuples from MCP servers</param>
        /// <param name="executor">Function to execute an external tool: (serverName, toolName, arguments) -> result string</param>
        public static void RegisterExternalTools(
            List<(string serverName, ExternalToolInfo tool)> tools,
            Func<string, string, Dictionary<string, string>, Task<string>> executor) {

            ExternalToolRegistry.Clear();
            _externalToolExecutor = executor;

            var sb = new StringBuilder();

            foreach (var (serverName, tool) in tools) {
                var qualifiedName = $"mcp_{serverName}_{tool.ToolName}";
                ExternalToolRegistry[qualifiedName] = tool;

                sb.AppendLine($"- <tool name=\"{qualifiedName}\">");
                sb.AppendLine($"    <description>[MCP Server: {serverName}] {tool.Description}</description>");
                sb.AppendLine($"    <parameters>");
                foreach (var param in tool.Parameters) {
                    sb.AppendLine($"        <parameter name=\"{param.Name}\" type=\"{param.Type}\" required=\"{param.Required.ToString().ToLower()}\">{param.Description}</parameter>");
                }
                sb.AppendLine($"    </parameters>");
                sb.AppendLine($"  </tool>");
            }

            _sCachedExternalToolsXml = sb.ToString();
            _sLogger?.LogInformation("Registered {Count} external MCP tools from {ServerCount} servers.",
                tools.Count, tools.Select(t => t.serverName).Distinct().Count());
        }

        /// <summary>
        /// Execute an external MCP tool by forwarding the call to the MCP server.
        /// </summary>
        private static async Task<object> ExecuteExternalToolAsync(string qualifiedToolName, ExternalToolInfo toolInfo, Dictionary<string, string> arguments) {
            if (_externalToolExecutor == null) {
                throw new InvalidOperationException("External tool executor is not configured.");
            }

            _sLogger?.LogInformation("Executing external MCP tool: {ToolName} on server {ServerName}", qualifiedToolName, toolInfo.ServerName);

            try {
                var result = await _externalToolExecutor(toolInfo.ServerName, toolInfo.ToolName, arguments);
                return result;
            } catch (Exception ex) {
                _sLogger?.LogError(ex, "Error executing external MCP tool: {ToolName}", qualifiedToolName);
                throw;
            }
        }

        /// <summary>
        /// Check if a tool name corresponds to an external MCP tool.
        /// </summary>
        public static bool IsExternalTool(string toolName) {
            return ExternalToolRegistry.ContainsKey(toolName);
        }

        /// <summary>
        /// Check if a tool name is registered (either built-in or external).
        /// </summary>
        public static bool IsToolRegistered(string toolName) {
            return ToolRegistry.ContainsKey(toolName) || ExternalToolRegistry.ContainsKey(toolName);
        }

        /// <summary>
        /// Register external MCP tools from a connected IMcpServerManager.
        /// This converts tool descriptions from the MCP server format to the McpToolHelper format
        /// and registers them for use in LLM prompts and tool calls.
        /// Can be called after server restart to refresh tool registrations.
        /// </summary>
        public static void RegisterExternalMcpTools(Interface.Mcp.IMcpServerManager mcpServerManager) {
            var externalTools = mcpServerManager.GetAllExternalTools();
            if (!externalTools.Any()) {
                // Clear stale registrations if no tools available
                ExternalToolRegistry.Clear();
                _sCachedExternalToolsXml = string.Empty;
                return;
            }

            var toolInfos = externalTools.Select(t => (t.serverName, new ExternalToolInfo {
                ServerName = t.serverName,
                ToolName = t.tool.Name,
                Description = t.tool.Description ?? "",
                Parameters = t.tool.InputSchema?.Properties?.Select(p =>
                    new ExternalToolParameter {
                        Name = p.Key,
                        Type = p.Value.Type ?? "string",
                        Description = p.Value.Description ?? "",
                        Required = t.tool.InputSchema.Required?.Contains(p.Key) ?? false
                    }).ToList() ?? new List<ExternalToolParameter>()
            })).ToList();

            RegisterExternalTools(
                toolInfos,
                async (serverName, toolName, arguments) => {
                    var objectArgs = arguments.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
                    var result = await mcpServerManager.CallToolAsync(serverName, toolName, objectArgs);
                    if (result.IsError) {
                        return $"Error: {string.Join("\n", result.Content?.Select(c => c.Text ?? "") ?? Enumerable.Empty<string>())}";
                    }
                    return string.Join("\n", result.Content?.Select(c => c.Text ?? "") ?? Enumerable.Empty<string>());
                });
        }
    }
}

using System;
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

namespace TelegramSearchBot.Service.AI.LLM
{
    /// <summary>
    /// Marks a method as an MCP tool that can be called by the LLM.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class McpToolAttribute : Attribute
    {
        /// <summary>
        /// A description of what the tool does.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Optional. If specified, this name will be used for the tool instead of the method name.
        /// </summary>
        public string Name { get; set; }

        public McpToolAttribute(string description)
        {
            Description = description;
        }
    }

    /// <summary>
    /// Describes a parameter of an McpTool.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class McpParameterAttribute : Attribute
    {
        /// <summary>
        /// A description of the parameter.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Indicates whether the parameter is required. Defaults to true.
        /// </summary>
        public bool IsRequired { get; set; } = true;

        public McpParameterAttribute(string description)
        {
            Description = description;
        }
    }

    /// <summary>
    /// Formats a list of methods (decorated with McpToolAttribute) into a string for the LLM prompt.
    /// </summary>
    public static class McpToolHelper
    {
        private static readonly Dictionary<string, (MethodInfo Method, Type OwningType)> ToolRegistry = new Dictionary<string, (MethodInfo, Type)>();
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

                if (ToolRegistry.ContainsKey(toolName))
                {
                    _logger?.LogWarning($"Duplicate tool name '{toolName}' found. Method {method.DeclaringType.FullName}.{method.Name} will be ignored.");
                    continue;
                }
                ToolRegistry[toolName] = (method, method.DeclaringType);

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
        
        public static bool TryParseToolCall(string xmlString, out string toolName, out Dictionary<string, string> arguments)
        {
            toolName = null;
            arguments = new Dictionary<string, string>();
            try
            {
                // Trim potential markdown code block fences
                xmlString = xmlString.Trim();
                if (xmlString.StartsWith("```xml")) xmlString = xmlString.Substring(6);
                if (xmlString.StartsWith("```")) xmlString = xmlString.Substring(3);
                if (xmlString.EndsWith("```")) xmlString = xmlString.Substring(0, xmlString.Length - 3);
                xmlString = xmlString.Trim();

                if (!xmlString.StartsWith("<") || !xmlString.EndsWith(">")) return false;

                var xDoc = XDocument.Parse(xmlString, LoadOptions.None);
                var rootElement = xDoc.Root;
                if (rootElement == null) return false;

                // Check if it's one of our registered tools by the root tag name
                if (!ToolRegistry.ContainsKey(rootElement.Name.LocalName))
                {
                     // It might be a generic <tool_name> wrapper as per original examples, let's check
                    if (rootElement.Name.LocalName == "tool_name" && rootElement.Elements().Count() == 1) {
                        rootElement = rootElement.Elements().First();
                        if (!ToolRegistry.ContainsKey(rootElement.Name.LocalName)) return false;
                    } else if (rootElement.Name.LocalName == "tool" && rootElement.Attribute("name") != null) {
                        // Or the format used in the prompt's tool list example
                        var nameAttr = rootElement.Attribute("name")?.Value;
                        if (nameAttr == null || !ToolRegistry.ContainsKey(nameAttr)) return false;
                        // If this format, parameters are expected under a <parameters> child
                        var paramsElement = rootElement.Element("parameters");
                        if (paramsElement != null) rootElement = paramsElement; // Process children of <parameters>
                        else { // If no <parameters> child, assume direct children are parameters
                            // This case is fine, loop below will handle it.
                        }
                        toolName = nameAttr;
                    }
                    else {
                        return false; // Not a recognized tool structure
                    }
                }
                
                toolName = toolName ?? rootElement.Name.LocalName;

                foreach (var element in rootElement.Elements())
                {
                    arguments[element.Name.LocalName] = element.Value;
                }
                return true;
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
    }
}

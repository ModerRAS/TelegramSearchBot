using System;

namespace TelegramSearchBot.Attributes // New namespace
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
}

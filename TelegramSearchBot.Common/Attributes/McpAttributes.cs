using System;

namespace TelegramSearchBot.Attributes {
    /// <summary>
    /// Marks a method as a tool that can be called by the LLM.
    /// Deprecated: Use <see cref="BuiltInToolAttribute"/> instead for built-in tools.
    /// </summary>
    [Obsolete("Use BuiltInToolAttribute instead. This attribute will be removed in a future version.")]
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class McpToolAttribute : Attribute {
        /// <summary>
        /// A description of what the tool does.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Optional. If specified, this name will be used for the tool instead of the method name.
        /// </summary>
        public string Name { get; set; } = null!;

        public McpToolAttribute(string description) {
            Description = description;
        }
    }

    /// <summary>
    /// Describes a parameter of an McpTool.
    /// Deprecated: Use <see cref="BuiltInParameterAttribute"/> instead.
    /// </summary>
    [Obsolete("Use BuiltInParameterAttribute instead. This attribute will be removed in a future version.")]
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class McpParameterAttribute : Attribute {
        /// <summary>
        /// A description of the parameter.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Indicates whether the parameter is required. Defaults to true.
        /// </summary>
        public bool IsRequired { get; set; } = true;

        public McpParameterAttribute(string description) {
            Description = description;
        }
    }
}

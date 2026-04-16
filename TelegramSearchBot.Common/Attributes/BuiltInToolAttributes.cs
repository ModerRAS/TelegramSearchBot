using System;

namespace TelegramSearchBot.Attributes {
    /// <summary>
    /// Marks a method as a built-in tool that can be called by the LLM.
    /// This replaces the previous McpToolAttribute for internal tools.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BuiltInToolAttribute : Attribute {
        /// <summary>
        /// A description of what the tool does.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Optional. If specified, this name will be used for the tool instead of the method name.
        /// </summary>
        public string Name { get; set; } = null!;

        public BuiltInToolAttribute(string description) {
            Description = description;
        }
    }

    /// <summary>
    /// Describes a parameter of a BuiltInTool.
    /// This replaces the previous McpParameterAttribute for internal tools.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class BuiltInParameterAttribute : Attribute {
        /// <summary>
        /// A description of the parameter.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Indicates whether the parameter is required. Defaults to true.
        /// </summary>
        public bool IsRequired { get; set; } = true;

        public BuiltInParameterAttribute(string description) {
            Description = description;
        }
    }
}

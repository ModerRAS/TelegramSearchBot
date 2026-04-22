using System.ComponentModel;

namespace TelegramSearchBot.Model.Mcp {
    public enum McpConfState {
        [Description("mcp_listing_servers")]
        ListingServers,

        [Description("mcp_adding_name")]
        AddingName,

        [Description("mcp_adding_command")]
        AddingCommand,

        [Description("mcp_adding_args")]
        AddingArgs,

        [Description("mcp_adding_env_key")]
        AddingEnvKey,

        [Description("mcp_adding_env_value")]
        AddingEnvValue,

        [Description("mcp_adding_timeout")]
        AddingTimeout,

        [Description("mcp_editing_select_server")]
        EditingSelectServer,

        [Description("mcp_editing_select_field")]
        EditingSelectField,

        [Description("mcp_editing_input_value")]
        EditingInputValue,

        [Description("mcp_editing_env_key")]
        EditingEnvKey,

        [Description("mcp_editing_env_value")]
        EditingEnvValue,

        [Description("mcp_deleting_select_server")]
        DeletingSelectServer,

        [Description("mcp_deleting_confirm")]
        DeletingConfirm
    }

    public static class McpConfStateExtensions {
        public static string GetDescription(this McpConfState state) {
            var fieldInfo = state.GetType().GetField(state.ToString());
            var attributes = ( DescriptionAttribute[] ) fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attributes.Length > 0 ? attributes[0].Description : state.ToString();
        }
    }
}

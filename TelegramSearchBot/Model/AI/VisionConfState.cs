using System.ComponentModel;

namespace TelegramSearchBot.Model.AI {
    public enum VisionConfState {
        [Description("vision_select_channel")]
        SelectChannel,

        [Description("vision_select_model")]
        SelectModel,

        [Description("vision_toggle")]
        ToggleVision,

        [Description("vision_view_channel")]
        ViewChannel
    }

    public static class VisionConfStateExtensions {
        public static string GetDescription(this VisionConfState state) {
            var fieldInfo = state.GetType().GetField(state.ToString());
            var attributes = ( DescriptionAttribute[] ) fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attributes.Length > 0 ? attributes[0].Description : state.ToString();
        }
    }
}

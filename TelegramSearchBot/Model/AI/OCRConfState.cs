using System.ComponentModel;

namespace TelegramSearchBot.Model.AI {
    public enum OCRConfState {
        [Description("main_menu")]
        MainMenu,

        [Description("selecting_engine")]
        SelectingEngine,

        [Description("selecting_llm_channel")]
        SelectingLLMChannel,

        [Description("selecting_llm_model")]
        SelectingLLMModel,

        [Description("viewing_config")]
        ViewingConfig
    }

    public static class OCRConfStateExtensions {
        public static string GetDescription(this OCRConfState state) {
            var fieldInfo = state.GetType().GetField(state.ToString());
            var attributes = (DescriptionAttribute[])fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attributes.Length > 0 ? attributes[0].Description : state.ToString();
        }
    }
}

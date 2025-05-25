using System.ComponentModel;

namespace TelegramSearchBot.Model.AI {
    public enum LLMConfState {
        [Description("setting_max_retry")]
        SettingMaxRetry,
        
        [Description("setting_max_image_retry")]
        SettingMaxImageRetry,
        
        [Description("setting_alt_photo_model")]
        SettingAltPhotoModel,
        
        [Description("awaiting_name")]
        AwaitingName,
        
        [Description("editing_select_channel")]
        EditingSelectChannel,
        
        [Description("adding_model_select_channel")]
        AddingModelSelectChannel,
        
        [Description("removing_model_select_channel")]
        RemovingModelSelectChannel,
        
        [Description("viewing_model_select_channel")]
        ViewingModelSelectChannel,
        
        [Description("awaiting_gateway")]
        AwaitingGateway,
        
        [Description("awaiting_provider")]
        AwaitingProvider,
        
        [Description("awaiting_parallel")]
        AwaitingParallel,
        
        [Description("awaiting_priority")]
        AwaitingPriority,
        
        [Description("awaiting_apikey")]
        AwaitingApiKey,
        
        [Description("editing_select_field")]
        EditingSelectField,
        
        [Description("editing_input_value")]
        EditingInputValue,
        
        [Description("adding_model_input")]
        AddingModelInput,
        
        [Description("removing_model_select")]
        RemovingModelSelect
    }

    public static class LLMConfStateExtensions {
        public static string GetDescription(this LLMConfState state) {
            var fieldInfo = state.GetType().GetField(state.ToString());
            var attributes = (DescriptionAttribute[])fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attributes.Length > 0 ? attributes[0].Description : state.ToString();
        }
    }
}
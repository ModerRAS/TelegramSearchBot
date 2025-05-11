using System;

namespace TelegramSearchBot.Service.Common
{
    public enum Feature
    {
        OrleansPipelineForTextMessages,
        OrleansPipelineForImageMessages,
        OrleansPipelineForAudioMessages,
        OrleansPipelineForVideoMessages,
        OrleansPipelineForCommands,
        OrleansPipelineForCallbackQueries
        // Add more features as needed
    }

    public static class FeatureToggleService
    {
        // Example: Read from environment variables or a config file.
        // For simplicity, we can start with hardcoded values or simple environment variable checks.
        // Environment variable names could be like "FEATURE_OrleansPipelineForTextMessages_ENABLED"

        public static bool IsEnabled(Feature feature)
        {
            string envVarName = $"FEATURE_{feature}_ENABLED";
            string envVarValue = Environment.GetEnvironmentVariable(envVarName);

            if (bool.TryParse(envVarValue, out bool isEnabled))
            {
                return isEnabled;
            }

            // Default behavior if environment variable is not set or invalid
            // For initial development, we might want to default some to 'false' or 'true'.
            // Let's default to false for now, so old pipeline remains active unless explicitly enabled.
            switch (feature)
            {
                // case Feature.OrleansPipelineForTextMessages:
                //     return true; // Example: Default a feature to true
                default:
                    return false; 
            }
        }

        // Helper to quickly check if any Orleans pipeline feature is enabled for a given message type
        public static bool IsOrleansPipelineActiveForMessageType(Telegram.Bot.Types.Enums.MessageType messageType)
        {
            switch (messageType)
            {
                case Telegram.Bot.Types.Enums.MessageType.Text:
                    return IsEnabled(Feature.OrleansPipelineForTextMessages) || IsEnabled(Feature.OrleansPipelineForCommands);
                case Telegram.Bot.Types.Enums.MessageType.Photo:
                    return IsEnabled(Feature.OrleansPipelineForImageMessages);
                case Telegram.Bot.Types.Enums.MessageType.Audio:
                case Telegram.Bot.Types.Enums.MessageType.Voice: // Voice is also audio
                    return IsEnabled(Feature.OrleansPipelineForAudioMessages);
                case Telegram.Bot.Types.Enums.MessageType.Video:
                    return IsEnabled(Feature.OrleansPipelineForVideoMessages);
                default:
                    return false;
            }
        }

        public static bool IsOrleansPipelineActiveForCallbackQuery()
        {
            return IsEnabled(Feature.OrleansPipelineForCallbackQueries);
        }
    }
}

using System;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.AI.LLM {
    public static class LlmContinuationSupport {
        public const string UnsupportedResumeMessage = "⚠️ 当前 provider 暂不支持继续迭代恢复。";

        public static bool SupportsResume(string provider) {
            return provider?.Trim() switch {
                "OpenAI" => true,
                "OpenAIResponses" => true,
                "Anthropic" => true,
                _ => false,
            };
        }

        public static bool SupportsResume(LLMProvider provider) {
            return provider is LLMProvider.OpenAI or LLMProvider.ResponsesAPI or LLMProvider.Anthropic;
        }

        public static string BuildUnsupportedResumeMessage(string provider, string modelName = null) {
            if (string.IsNullOrWhiteSpace(provider) && string.IsNullOrWhiteSpace(modelName)) {
                return UnsupportedResumeMessage;
            }

            if (string.IsNullOrWhiteSpace(modelName)) {
                return $"⚠️ 当前 provider（{provider}）暂不支持继续迭代恢复。";
            }

            return $"⚠️ 当前 provider（{provider} / {modelName}）暂不支持继续迭代恢复。";
        }
    }
}

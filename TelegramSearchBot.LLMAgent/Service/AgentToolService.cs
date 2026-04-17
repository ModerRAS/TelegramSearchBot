using TelegramSearchBot.Attributes;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.LLMAgent.Service {
    public sealed class AgentToolService {
        private readonly ToolExecutor _toolExecutor;

        public AgentToolService(ToolExecutor toolExecutor) {
            _toolExecutor = toolExecutor;
        }

        [BuiltInTool("Return the input text unchanged.", Name = "echo")]
        public Task<string> EchoAsync([BuiltInParameter("Text to echo back.")] string text) {
            return _toolExecutor.EchoAsync(text);
        }

        [BuiltInTool("Evaluate a simple arithmetic expression.", Name = "calculator")]
        public Task<string> CalculatorAsync([BuiltInParameter("Arithmetic expression such as 1+2*3.")] string expression) {
            return _toolExecutor.CalculateAsync(expression);
        }

        [BuiltInTool("Send a plain text Telegram message via the main process.", Name = "send_message")]
        public Task<string> SendMessageAsync(
            [BuiltInParameter("Target Telegram chat ID.")] long chatId,
            [BuiltInParameter("Message text to send.")] string text,
            ToolContext toolContext) {
            return _toolExecutor.SendMessageAsync(chatId, text, toolContext?.UserId ?? 0, toolContext?.MessageId ?? 0, CancellationToken.None);
        }
    }
}

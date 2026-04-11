using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model.AI {
    public enum LLMProvider {
        None,
        OpenAI,
        Ollama,
        Gemini,
        MiniMax = 4,
        LMStudio = 5,
        Anthropic = 6
    }
}

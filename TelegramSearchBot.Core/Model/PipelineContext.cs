using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace TelegramSearchBot.Core.Model {
    public class PipelineContext {
        public Update Update { get; set; }
        public Dictionary<string, dynamic> PipelineCache { get; set; }
        public long MessageDataId { get; set; }
        public BotMessageType BotMessageType { get; set; }
        public List<string> ProcessingResults { get; set; } = new List<string>();
    }
    public enum BotMessageType {
        Unknown,
        Message,
        CallbackQuery
    }
}

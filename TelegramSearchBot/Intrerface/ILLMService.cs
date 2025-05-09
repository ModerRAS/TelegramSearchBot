using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using System.Threading; // Added for CancellationToken

namespace TelegramSearchBot.Intrerface {
    public interface ILLMService {
        public IAsyncEnumerable<string> ExecAsync(Message message, long ChatId, string modelName, LLMChannel channel, 
                                                  [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default);
    }
}

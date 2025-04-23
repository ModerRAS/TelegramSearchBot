using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;

namespace TelegramSearchBot.Service {
    public class CloudPasteService : IService {
        public string ServiceName => "CloudPasteService";

        public async Task<string> ExecAsync() {
            return string.Empty;
        }
    }
}

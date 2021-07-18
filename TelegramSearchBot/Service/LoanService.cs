using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service {
    class LoanService : IService {
        public string ServiceName { get => "LoanService"; }

        public Task ExecuteAsync(MessageOption messageOption) {
            throw new NotImplementedException();
        }
    }
}

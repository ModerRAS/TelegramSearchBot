using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SearchServer {
    public class MessagerService : Messager.MessagerBase {
        private readonly ILogger<MessagerService> _logger;
        public MessagerService(ILogger<MessagerService> logger) {
            _logger = logger;
        }

        public override Task<Reply> AddMessage(MessageOption messageOption, ServerCallContext context) {
            return Task.FromResult(new Reply {
                Message = "Sucess"
            });
        }
    }
}

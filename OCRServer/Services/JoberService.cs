using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OCRServer.Services {
    public class JoberService : Jober.JoberBase {
        private readonly ILogger<JoberService> _logger;
        public JoberService(ILogger<JoberService> logger) {
            _logger = logger;
        }

        public override Task<Reply> SayHello(Request request, ServerCallContext context) {
            return Task.FromResult(new Reply {
                Message = "Hello "
            });
        }
    }
}

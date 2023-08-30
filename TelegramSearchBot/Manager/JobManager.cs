using LiteDB;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Common.Interface;

namespace TelegramSearchBot.Manager {
    public class JobManager<S, R> where S:ICompareRPC where R:ICompareRPC {
        private ConcurrentQueue<S> SQueue { get; init; }
        private ConcurrentQueue<R> RQueue { get; init; }
        private ILogger<JobManager<S,R>> logger { get; set; }
        public JobManager(ILogger<JobManager<S, R>> logger) {
            SQueue = new ConcurrentQueue<S>();
            RQueue = new ConcurrentQueue<R>();
            this.logger = logger;
        }
        public async Task WaitAndSave() {
            await Task.Delay(Env.TaskDelayTimeout);
        }
        public async Task<R> Execute(S item) {
            logger.LogInformation($"Execute {item.GetUniqueId()}");
            SQueue.Enqueue(item);
            while (true) {
                if (RQueue.TryPeek(out var r)) {
                    if (r.GetUniqueId().Equals(item.GetUniqueId())) {
                        RQueue.TryDequeue(out var result);
                        return result;
                    }
                }
                await WaitAndSave();
            }
        }
        public void Add(R item) {
            RQueue.Enqueue(item);
        }
        public async Task<S> GetAsync() {
            while (true) {
                if (SQueue.TryDequeue(out var item)) {
                    logger.LogInformation($"Get Item {item.GetUniqueId()}");
                    return item;
                } else {
                    await WaitAndSave();
                }
            }
        }
    }
}

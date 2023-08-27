using LiteDB;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Common.Interface;

namespace TelegramSearchBot.Manager {
    public class JobManager<S, R> where S:ICompareRPC where R:ICompareRPC {
        private ConcurrentQueue<S> SQueue { get; init; }
        private ConcurrentQueue<R> RQueue { get; init; }
        public JobManager() {
            SQueue = new ConcurrentQueue<S>();
            RQueue = new ConcurrentQueue<R>();
        }
        public async Task WaitAndSave() {
            await Task.Delay(Env.TaskDelayTimeout);
        }
        public async Task<R> Execute(S item) {
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
                    return item;
                } else {
                    await WaitAndSave();
                }
            }
        }
    }
}

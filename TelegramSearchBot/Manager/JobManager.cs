using LiteDB;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Common.Interface;

namespace TelegramSearchBot.Manager {
    public class JobManager<S, R> where S:ICompareRPC where R:ICompareRPC {
        private ConcurrentQueue<S> SQueue { get; init; }
        private ConcurrentQueue<R> RQueue { get; init; }
        private LiteDatabase Cache { get; init; } = Env.Cache;
        private ILiteCollection<ConcurrentQueue<S>> SQ { get; set; }
        private ILiteCollection<ConcurrentQueue<R>> RQ { get; set; }
        public JobManager() {
            SQ = Cache.GetCollection<ConcurrentQueue<S>>("SQueue");
            RQ = Cache.GetCollection<ConcurrentQueue<R>>("RQueue");
            var SQList = SQ.FindAll().ToList();
            if (SQList.Count > 0) {
                SQueue = SQList[0];
            } else {
                SQueue = new ConcurrentQueue<S>();
            }
            var RQList = RQ.FindAll().ToList();
            if (RQList.Count > 0) {
                RQueue = RQList[0];
            } else {
                RQueue = new ConcurrentQueue<R>();
            }
        }
        public async Task WaitAndSave() {
            var SQ = Cache.GetCollection<ConcurrentQueue<S>>("SQueue");
            SQ.DeleteAll();
            SQ.Insert(SQueue);
            var RQ = Cache.GetCollection<ConcurrentQueue<R>>("RQueue");
            RQ.DeleteAll();
            RQ.Insert(RQueue);
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

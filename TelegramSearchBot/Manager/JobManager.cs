using LiteDB;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelegramSearchBot.Common.Interface;

namespace TelegramSearchBot.Manager {
    [Obsolete]
    public class JobManager<S, R> where S : ICompareRPC where R : ICompareRPC
    {
        private ConcurrentQueue<S> SQueue { get; init; }
        private ConcurrentDictionary<string, R> RDictionary { get; init; }
        private ILogger<JobManager<S, R>> logger { get; set; }
        private int Count = 0;
        public JobManager(ILogger<JobManager<S, R>> logger)
        {
            SQueue = new ConcurrentQueue<S>();
            RDictionary = new ConcurrentDictionary<string, R>();
            this.logger = logger;
        }
        public async Task WaitAndSave()
        {
            if (Count++ % 100 == 0)
            {
                logger.LogInformation($"Still in Result:{RDictionary.Count}");
                Count = 0;
            }
            await Task.Delay(Env.TaskDelayTimeout);
        }
        public async Task<R> Execute(S item)
        {
            logger.LogInformation($"Execute {item.GetUniqueId()}");
            SQueue.Enqueue(item);
            while (true)
            {
                if (RDictionary.TryRemove(item.GetUniqueId(), out var result))
                {
                    return result;
                }
                await WaitAndSave();
            }
        }
        public void Add(R item)
        {
            if (RDictionary.TryAdd(item.GetUniqueId(), item))
            {
                logger.LogInformation($"Add Success {item.GetUniqueId()}");
            }
            else
            {
                logger.LogInformation($"Add Failed {item.GetUniqueId()}");
            }
        }
        public async Task<S> GetAsync()
        {
            while (true)
            {
                if (SQueue.TryDequeue(out var item))
                {
                    logger.LogInformation($"Get Item {item.GetUniqueId()}");
                    return item;
                }
                else
                {
                    await WaitAndSave();
                }
            }
        }
    }
}

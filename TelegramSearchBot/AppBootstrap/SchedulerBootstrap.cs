using Garnet;
using Garnet.server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramSearchBot.AppBootstrap {
    public class SchedulerBootstrap : AppBootstrap {
        public static void Startup(string[] args) {
            if (args.Length != 2 || !args[0].Equals("Scheduler")) {
                return;
            }
            try {
                using var server = new GarnetServer(["--port", args[1]]);
                server.Start();
                Thread.Sleep(Timeout.Infinite);
            } catch (Exception ex) {
                Console.WriteLine($"Unable to initialize server due to exception: {ex.Message}");
            }
        }
    }
}

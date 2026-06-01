using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Garnet;
using Garnet.server;
using Serilog;

namespace TelegramSearchBot.AppBootstrap {
    public class SchedulerBootstrap : AppBootstrap {
        internal static string[] BuildGarnetArguments(string port) =>
            ["--bind", "127.0.0.1", "--port", port, "--lua", "--lua-transaction-mode"];

        public static void Startup(string[] args) {
            if (args.Length != 2 || !args[0].Equals("Scheduler")) {
                return;
            }
            try {
                using var server = new GarnetServer(BuildGarnetArguments(args[1]));
                server.Start();
                Thread.Sleep(Timeout.Infinite);
            } catch (Exception ex) {
                Log.Error(ex, "Unable to initialize server due to exception: {ErrorMessage}", ex.Message);
            }
        }
    }
}

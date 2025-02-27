using Garnet.client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.AppBootstrap {
    public class OCRBootstrap : AppBootstrap {
        public static void Startup(string[] args) {
            using var garnet = new GarnetClient(new IPEndPoint(IPAddress.Loopback, int.Parse(args[1])));
            
        }
    }
}

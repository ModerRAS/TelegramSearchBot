using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interface {
    public interface IStreamService {
        public abstract Task<string> ExecuteAsync(Stream file);
    }
}

﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Intrerface {
    public interface IStreamService : IService {
        public abstract Task<string> ExecuteAsync(Stream file);
    }
}
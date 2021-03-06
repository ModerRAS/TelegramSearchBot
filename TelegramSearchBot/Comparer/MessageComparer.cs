﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Comparer {
    class MessageComparer : IEqualityComparer<Message> {
        public bool Equals(Message x, Message y) {
            return x.GroupId == y.GroupId && x.MessageId == y.MessageId;
        }

        public int GetHashCode([DisallowNull] Message obj) {
            return obj.GetHashCode();
        }
    }
}

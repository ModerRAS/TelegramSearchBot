using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.CommonModel;

namespace TelegramSearchBot.Comparer {
    class MessageComparer : IEqualityComparer<Message> {
        public bool Equals(Message x, Message y) {
            return x.GroupId == y.GroupId && x.MessageId == y.MessageId && x.Content.Equals(y.Content);
        }

        public int GetHashCode([DisallowNull] Message obj) {
            return obj.GroupId.GetHashCode() * obj.MessageId.GetHashCode() * obj.Content.GetHashCode();
        }
    }
}

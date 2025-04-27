using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Comparer
{
    class MessageComparer : IEqualityComparer<Message> {
        public bool Equals(Message x, Message y) {
            return x.GroupId == y.GroupId && x.MessageId == y.MessageId && x.Content.Equals(y.Content);
        }

        public int GetHashCode([DisallowNull] Message obj) {
            return obj.GroupId.GetHashCode() * obj.MessageId.GetHashCode() * obj.Content.GetHashCode();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model {
    public enum LoanType {
        Init,
        Change,
        Get
    }
    public class LoanOption {
        public long ChatId { get; set; }
        public long MoneyFromUserId { get; set; }
        public long MoneyToUserId { get; set; }
        public double MoneyCount { get; set; }
        public int LoanTerm { get; set; }
        public double MonthlyInterestRate { get; set; }
        public long MessageId { get; set; }
        public LoanType Type { get; set; }
        public DateTime DateTime { get; set; }
    }
}

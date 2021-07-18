using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model {
    public class Loan {
        [Key]
        public long Id { get; set; }
        public long GroupId { get; set; }
        public long MessageId { get; set; }
        public long MoneyFromUserId { get; set; }
        public long MoneyToUserId { get; set; }
        public double MoneyCount { get; set; }
        public int LoanTerm { get; set; }
        public double MonthlyInterestRate { get; set; }
        public DateTime DateTime { get; set; }
    }
}

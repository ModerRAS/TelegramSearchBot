using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace TelegramSearchBot.Model.Data {
    public class UserWithGroup {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public long GroupId { get; set; }
        public long UserId { get; set; }
    }
}

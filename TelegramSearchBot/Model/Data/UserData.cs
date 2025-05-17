using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Model.Data
{
    public class UserData
    {
        [Key]
        public long Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string UserName { get; set; }
        public bool? IsPremium { get; set; }
        public bool? IsBot { get; set; }

        // 导航属性
        public virtual ICollection<Message> FromMessages { get; set; }
        public virtual ICollection<Message> ReplyToMessages { get; set; }
    }
}

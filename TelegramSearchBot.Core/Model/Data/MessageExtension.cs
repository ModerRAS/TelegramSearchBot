using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramSearchBot.Core.Model.Data {
    public class MessageExtension {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [ForeignKey(nameof(Message))]
        public long MessageDataId { get; set; }

        public string Name { get; set; }
        public string Value { get; set; }

        public virtual Message Message { get; set; }
    }
}

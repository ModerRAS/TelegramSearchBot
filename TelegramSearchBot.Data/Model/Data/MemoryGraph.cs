using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramSearchBot.Model.Data
{
    public class MemoryGraph
    {
        [Key]
        public long Id { get; set; }
        
        [Required]
        public long ChatId { get; set; }
        
        [Required]
        public string Name { get; set; }
        
        [Required]
        public string EntityType { get; set; }
        
        public string Observations { get; set; }
        
        public string FromEntity { get; set; }
        
        public string ToEntity { get; set; }
        
        public string RelationType { get; set; }
        
        [Required]
        public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
        
        [Required]
        public string ItemType { get; set; } // "entity" or "relation"
    }
}
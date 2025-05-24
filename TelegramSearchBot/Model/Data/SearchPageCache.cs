using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Model.Data
{
    public class SearchPageCache
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        [Required]
        public string UUID { get; set; }
        
        [Required]
        public string SearchOptionJson { get; set; }
        public DateTime CreatedTime { get; set; } = DateTime.UtcNow;

        [NotMapped]
        private SearchOption _searchOptionCache;
        [NotMapped]
        public SearchOption SearchOption
        {
            get
            {
                if (_searchOptionCache == null && SearchOptionJson != null)
                {
                    _searchOptionCache = JsonConvert.DeserializeObject<SearchOption>(SearchOptionJson);
                }
                return _searchOptionCache;
            }
            set
            {
                _searchOptionCache = value;
                SearchOptionJson = value != null ? JsonConvert.SerializeObject(value) : null;
            }
        }
    }
}
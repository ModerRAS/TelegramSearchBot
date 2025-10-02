using System.ComponentModel.DataAnnotations;

namespace TelegramSearchBot.Model {
    public class TokenModel {
        [Key]
        public int Id { get; set; }
        public string Type { get; set; }
        public string Token { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;

namespace TelegramSearchBot.Core.Model.Data;

public class AppConfigurationItem {
    [Key]
    public string Key { get; set; }

    public string Value { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace TelegramSearchBot.Model.Data;

public class AppConfigurationItem
{
    [Key]
    public string Key { get; set; }

    public string Value { get; set; }
}

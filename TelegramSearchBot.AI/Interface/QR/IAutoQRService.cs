using System.Threading.Tasks;

namespace TelegramSearchBot.Interface.AI.QR
{
    public interface IAutoQRService
    {
        Task<string> ExecuteAsync(string path);
    }
}

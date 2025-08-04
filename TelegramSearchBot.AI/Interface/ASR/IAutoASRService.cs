using System.Threading.Tasks;

namespace TelegramSearchBot.Interface.AI.ASR
{
    public interface IAutoASRService
    {
        Task<string> ExecuteAsync(string path);
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Interface.Vector
{
    public interface IVectorGenerationService
    {
        Task<SearchOption> Search(SearchOption searchOption);
        Task<float[]> GenerateVectorAsync(string text);
        Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> Payload);
        Task StoreVectorAsync(string collectionName, float[] vector, long MessageId);
        Task StoreMessageAsync(Message message);
        Task<float[][]> GenerateVectorsAsync(IEnumerable<string> texts);
        Task<bool> IsHealthyAsync();
    }
} 
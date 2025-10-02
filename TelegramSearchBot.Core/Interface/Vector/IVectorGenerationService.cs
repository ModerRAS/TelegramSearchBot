using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Core.Model.Data;
using ModelSearchOption = TelegramSearchBot.Core.Model.SearchOption;

namespace TelegramSearchBot.Core.Interface.Vector {
    public interface IVectorGenerationService {
        Task<ModelSearchOption> Search(ModelSearchOption searchOption);
        Task<float[]> GenerateVectorAsync(string text);
        Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> Payload);
        Task StoreVectorAsync(string collectionName, float[] vector, long MessageId);
        Task StoreMessageAsync(Message message);
        Task<float[][]> GenerateVectorsAsync(IEnumerable<string> texts);
        Task<bool> IsHealthyAsync();
    }
}

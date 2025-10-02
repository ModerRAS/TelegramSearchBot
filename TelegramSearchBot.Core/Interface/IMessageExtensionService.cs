using System.Collections.Generic;
using System.Threading.Tasks;
using TelegramSearchBot.Core.Model.Data;

namespace TelegramSearchBot.Core.Interface {
    public interface IMessageExtensionService : IService {
        Task<MessageExtension> GetByIdAsync(int id);
        Task<List<MessageExtension>> GetByMessageDataIdAsync(long messageDataId);
        Task AddOrUpdateAsync(MessageExtension extension);
        Task AddOrUpdateAsync(long messageDataId, string name, string value);
        Task DeleteAsync(int id);
        Task DeleteByMessageDataIdAsync(long messageDataId);
        Task<long?> GetMessageIdByMessageIdAndGroupId(long messageId, long groupId);
    }
}

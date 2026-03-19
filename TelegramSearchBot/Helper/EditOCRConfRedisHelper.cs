using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace TelegramSearchBot.Helper {
    public class EditOCRConfRedisHelper {
        private readonly IConnectionMultiplexer _connection;
        private readonly long _chatId;
        private IDatabase _database;

        public EditOCRConfRedisHelper(IConnectionMultiplexer connection, long chatId) {
            _connection = connection;
            _chatId = chatId;
            _database = _connection.GetDatabase();
        }

        private IDatabase GetDatabase() => _database;

        private string StateKey => $"ocrconf:{_chatId}:state";
        private string DataKey => $"ocrconf:{_chatId}:data";
        private string ChannelKey => $"ocrconf:{_chatId}:channel";

        public async Task<string> GetStateAsync() {
            return await GetDatabase().StringGetAsync(StateKey);
        }

        public async Task SetStateAsync(string state) {
            await GetDatabase().StringSetAsync(StateKey, state);
        }

        public async Task<string> GetDataAsync() {
            return await GetDatabase().StringGetAsync(DataKey);
        }

        public async Task SetDataAsync(string data) {
            await GetDatabase().StringSetAsync(DataKey, data);
        }

        public async Task<int?> GetChannelIdAsync() {
            var value = await GetDatabase().StringGetAsync(ChannelKey);
            if (value.HasValue && int.TryParse(value.ToString(), out var channelId)) {
                return channelId;
            }
            return null;
        }

        public async Task SetChannelIdAsync(int channelId) {
            await GetDatabase().StringSetAsync(ChannelKey, channelId.ToString());
        }

        public async Task DeleteKeysAsync() {
            var db = GetDatabase();
            await db.KeyDeleteAsync(StateKey);
            await db.KeyDeleteAsync(DataKey);
            await db.KeyDeleteAsync(ChannelKey);
        }
    }
}

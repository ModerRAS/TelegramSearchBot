using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace TelegramSearchBot.Helper {
    public class EditLLMConfRedisHelper {
        private readonly IConnectionMultiplexer _connection;
        private readonly long _chatId;
        private IDatabase _database;

        public EditLLMConfRedisHelper(IConnectionMultiplexer connection, long chatId) {
            _connection = connection;
            _chatId = chatId;
            _database = _connection.GetDatabase();
        }

        private IDatabase GetDatabase() => _database;

        private string StateKey => $"llmconf:{_chatId}:state";
        private string DataKey => $"llmconf:{_chatId}:data";

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

        public async Task DeleteKeysAsync() {
            var db = GetDatabase();
            await db.KeyDeleteAsync(StateKey);
            await db.KeyDeleteAsync(DataKey);
        }
    }
}

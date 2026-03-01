using Newtonsoft.Json;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Extension {
    public static class SearchPageCacheExtensions {
        public static SearchOption GetSearchOption(this SearchPageCache cache) {
            if (cache.SearchOptionJson == null) {
                return null;
            }
            return JsonConvert.DeserializeObject<SearchOption>(cache.SearchOptionJson);
        }

        public static void SetSearchOption(this SearchPageCache cache, SearchOption searchOption) {
            cache.SearchOptionJson = searchOption != null ? JsonConvert.SerializeObject(searchOption) : null;
        }
    }
}

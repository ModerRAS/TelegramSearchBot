using System;
using System.Threading.Tasks;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Service.Search {
    public enum CallbackActionType {
        NextPage,
        DeleteHistory,
        ChangeSearchType
    }

    public class CallbackData {
        public CallbackActionType ActionType { get; set; }
        public SearchType? NewSearchType { get; set; }
        public string OriginalUuid { get; set; }
    }

    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class CallbackDataService : IService {
        public string ServiceName => "CallbackDataService";

        private readonly SearchOptionStorageService _searchOptionStorageService;

        public CallbackDataService(SearchOptionStorageService searchOptionStorageService) {
            _searchOptionStorageService = searchOptionStorageService;
        }

        /// <summary>
        /// 生成下一页的回调数据
        /// </summary>
        public async Task<string> GenerateNextPageCallbackAsync(SearchOption searchOption) {
            var nextOption = _searchOptionStorageService.GetNextSearchOption(searchOption);
            if (nextOption == null) return null;

            return await _searchOptionStorageService.SetSearchOptionAsync(nextOption);
        }

        /// <summary>
        /// 生成删除历史的回调数据
        /// </summary>
        public async Task<string> GenerateDeleteHistoryCallbackAsync(SearchOption searchOption) {
            var deleteOption = _searchOptionStorageService.GetToDeleteNowSearchOption(searchOption);
            return await _searchOptionStorageService.SetSearchOptionAsync(deleteOption);
        }

        /// <summary>
        /// 生成切换搜索方式的回调数据
        /// </summary>
        public async Task<string> GenerateChangeSearchTypeCallbackAsync(SearchOption searchOption, SearchType newSearchType) {
            // 深拷贝搜索选项
            var newOption = Newtonsoft.Json.JsonConvert.DeserializeObject<SearchOption>(
                Newtonsoft.Json.JsonConvert.SerializeObject(searchOption));

            // 修改搜索类型，重置分页
            newOption.SearchType = newSearchType;
            newOption.Skip = 0;
            newOption.Count = -1; // 重新计算总数
            newOption.Messages = null; // 重置结果

            return await _searchOptionStorageService.SetSearchOptionAsync(newOption);
        }

        /// <summary>
        /// 解析回调数据（用于向后兼容现有的UUID方式）
        /// </summary>
        public async Task<SearchOption> ParseCallbackDataAsync(string callbackData) {
            return await _searchOptionStorageService.GetAndRemoveSearchOptionAsync(callbackData);
        }
    }
}

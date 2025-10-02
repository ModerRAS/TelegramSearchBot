using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Interface.Vector;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.Vector {
    /// <summary>
    /// 对话向量服务 - 现在只处理对话段而不再直接操作向量存储
    /// 实际的向量存储交给FaissVectorService处理
    /// </summary>
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class ConversationVectorService : IService, IVectorGenerationService {
        public string ServiceName => "ConversationVectorService";

        private readonly ILogger<ConversationVectorService> _logger;
        private readonly DataDbContext _dataDbContext;
        private readonly IGeneralLLMService _generalLLMService;
        private readonly ConversationSegmentationService _segmentationService;
        private readonly FaissVectorService _faissVectorService;

        public ConversationVectorService(
            ILogger<ConversationVectorService> logger,
            DataDbContext dataDbContext,
            IGeneralLLMService generalLLMService,
            ConversationSegmentationService segmentationService,
            FaissVectorService faissVectorService) {
            _logger = logger;
            _dataDbContext = dataDbContext;
            _generalLLMService = generalLLMService;
            _segmentationService = segmentationService;
            _faissVectorService = faissVectorService;
        }

        /// <summary>
        /// 向量搜索 - 委托给FaissVectorService
        /// </summary>
        public async Task<SearchOption> Search(SearchOption searchOption) {
            return await _faissVectorService.Search(searchOption);
        }

        /// <summary>
        /// 为对话段生成向量 - 委托给FaissVectorService
        /// </summary>
        public async Task VectorizeConversationSegment(ConversationSegment segment) {
            try {
                await _faissVectorService.VectorizeConversationSegment(segment);
                _logger.LogDebug($"对话段 {segment.Id} 向量化成功");
            } catch (Exception ex) {
                _logger.LogError(ex, $"对话段 {segment.Id} 向量化失败");
                throw;
            }
        }

        /// <summary>
        /// 批量处理群组对话段
        /// </summary>
        public async Task ProcessGroupConversationSegments(long groupId) {
            try {
                // 首先确保对话段已创建
                await _segmentationService.CreateSegmentsForGroupAsync(groupId);

                // 然后进行向量化
                await _faissVectorService.VectorizeGroupSegments(groupId);

                _logger.LogInformation($"群组 {groupId} 对话段处理完成");
            } catch (Exception ex) {
                _logger.LogError(ex, $"群组 {groupId} 对话段处理失败");
                throw;
            }
        }

        #region IVectorGenerationService 实现 - 委托给FaissVectorService

        public async Task<float[]> GenerateVectorAsync(string text) {
            return await _faissVectorService.GenerateVectorAsync(text);
        }

        public async Task StoreVectorAsync(string collectionName, ulong id, float[] vector, Dictionary<string, string> payload) {
            await _faissVectorService.StoreVectorAsync(collectionName, id, vector, payload);
        }

        public async Task StoreVectorAsync(string collectionName, float[] vector, long messageId) {
            await _faissVectorService.StoreVectorAsync(collectionName, vector, messageId);
        }

        public async Task StoreMessageAsync(Message message) {
            await _faissVectorService.StoreMessageAsync(message);
        }

        public async Task<float[][]> GenerateVectorsAsync(IEnumerable<string> texts) {
            return await _faissVectorService.GenerateVectorsAsync(texts);
        }

        public async Task<bool> IsHealthyAsync() {
            return await _faissVectorService.IsHealthyAsync();
        }

        #endregion
    }
}

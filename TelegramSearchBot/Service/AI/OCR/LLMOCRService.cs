using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface.AI.OCR;
using TelegramSearchBot.Interface.AI.LLM;

namespace TelegramSearchBot.Service.AI.OCR {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class LLMOCRService : IOCRService {
        private readonly IGeneralLLMService _generalLLMService;
        private readonly ILogger<LLMOCRService> _logger;

        public OCREngine Engine => OCREngine.LLM;

        public LLMOCRService(
            IGeneralLLMService generalLLMService,
            ILogger<LLMOCRService> logger
            ) {
            _generalLLMService = generalLLMService;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(Stream file) {
            try {
                var tg_img = SKBitmap.Decode(file);
                var tg_img_data = tg_img.Encode(SKEncodedImageFormat.Jpeg, 99);
                var tempPath = Path.GetTempFileName() + ".jpg";
                
                try {
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write)) {
                        tg_img_data.SaveTo(fs);
                    }

                    _logger.LogInformation("正在使用LLM进行OCR识别...");
                    var result = await _generalLLMService.AnalyzeImageAsync(tempPath, 0);

                    if (string.IsNullOrWhiteSpace(result)) {
                        _logger.LogWarning("LLM OCR返回空结果");
                        return string.Empty;
                    }

                    _logger.LogInformation("LLM OCR识别完成");
                    return result;
                } finally {
                    if (File.Exists(tempPath)) {
                        File.Delete(tempPath);
                    }
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "LLM OCR处理失败");
                throw;
            }
        }
    }
}

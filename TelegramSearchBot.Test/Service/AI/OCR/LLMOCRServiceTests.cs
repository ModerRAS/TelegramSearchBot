using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SkiaSharp;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Service.AI.LLM;
using TelegramSearchBot.Service.AI.OCR;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.OCR {
    public class LLMOCRServiceTests {
        [Fact]
        public async Task ExecuteAsync_UsesVisionOcrPrompt() {
            var llmServiceMock = new Mock<IGeneralLLMService>();
            llmServiceMock
                .Setup(s => s.AnalyzeImageAsync(
                    It.Is<string>(path => path.EndsWith(".jpg")),
                    0,
                    GeneralLLMService.DefaultVisionOcrPrompt,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("recognized text");

            var loggerMock = new Mock<ILogger<LLMOCRService>>();
            var service = new LLMOCRService(llmServiceMock.Object, loggerMock.Object);

            using var bitmap = new SKBitmap(8, 8);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());

            var result = await service.ExecuteAsync(stream);

            Assert.Equal("recognized text", result);
            llmServiceMock.Verify(s => s.AnalyzeImageAsync(
                It.Is<string>(path => path.EndsWith(".jpg")),
                0,
                GeneralLLMService.DefaultVisionOcrPrompt,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using SkiaSharp;
using StackExchange.Redis;
using TelegramSearchBot.Service.AI.OCR;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.OCR {
    public class LongImageOcrSlicerTests {
        [Theory]
        [InlineData(1000, 4096)]
        [InlineData(3000, 5000)]
        [InlineData(1600, 4000)]
        public void PlanSlices_NonLongImage_UsesSingleSlice(int width, int height) {
            using var image = new SKBitmap(width, height);

            var slices = LongImageOcrSlicer.PlanSlices(image);

            var slice = Assert.Single(slices);
            Assert.Equal(new OcrImageSlice(0, height, false), slice);
        }

        [Fact]
        public void PlanSlices_PrefersBlankBandNearTarget() {
            using var image = CreateDenseImage(800, 7000);
            FillRows(image, 3000, 3120, SKColors.White);

            var slices = LongImageOcrSlicer.PlanSlices(image);

            Assert.True(slices.Count > 1);
            Assert.InRange(slices[0].Top + slices[0].Height, 3000, 3119);
            Assert.False(slices[0].HasOverlap);
            Assert.False(slices[1].HasOverlap);
            AssertCompleteCoverage(slices, image.Height);
        }

        [Fact]
        public void PlanSlices_WithoutBlankBand_UsesOverlapAndCoversImage() {
            using var image = CreateDenseImage(800, 9000);

            var slices = LongImageOcrSlicer.PlanSlices(image);

            Assert.True(slices.Count >= 3);
            Assert.Contains(slices, slice => slice.HasOverlap);
            Assert.All(slices, slice => Assert.InRange(slice.Height, 1, LongImageOcrSlicer.LongImageHeightThreshold));
            AssertCompleteCoverage(slices, image.Height);
            for (var index = 1; index < slices.Count; index++) {
                Assert.True(slices[index].Top < slices[index - 1].Top + slices[index - 1].Height);
            }
        }

        [Fact]
        public void MergeResults_RemovesRepeatedBoundaryLines() {
            var result = LongImageOcrSlicer.MergeResults([
                "第一段 重叠块一 重叠块二",
                "重叠块一 重叠块二 第二段",
                "第三段"
            ]);

            Assert.Equal("第一段 重叠块一 重叠块二 第二段 第三段", result);
        }

        [Fact]
        public async Task ExecuteAsync_LongImage_RunsSlicesInOrderAndMergesResults() {
            var calls = new List<(int Width, int Height)>();
            var responses = new Queue<string>(["第一段 重叠块", "重叠块 第二段"]);
            var service = new PaddleOCRService(
                Mock.Of<IConnectionMultiplexer>(),
                payload => {
                    using var bitmap = SKBitmap.Decode(Convert.FromBase64String(payload));
                    calls.Add((bitmap.Width, bitmap.Height));
                    return Task.FromResult(responses.Dequeue());
                });
            using var stream = Encode(CreateWhiteImage(1000, 5000));

            var result = await service.ExecuteAsync(stream);

            Assert.Equal(2, calls.Count);
            Assert.All(calls, call => Assert.Equal(1000, call.Width));
            Assert.All(calls, call => Assert.InRange(call.Height, 1, LongImageOcrSlicer.LongImageHeightThreshold));
            Assert.Equal("第一段 重叠块 第二段", result);
        }

        [Fact]
        public async Task ExecuteAsync_RegularImage_RunsSingleRpc() {
            var calls = 0;
            var service = new PaddleOCRService(
                Mock.Of<IConnectionMultiplexer>(),
                payload => {
                    calls++;
                    return Task.FromResult("识别结果");
                });
            using var stream = Encode(CreateWhiteImage(1200, 2000));

            var result = await service.ExecuteAsync(stream);

            Assert.Equal("识别结果", result);
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task ExecuteAsync_WhenSliceFails_StopsProcessing() {
            var calls = 0;
            var service = new PaddleOCRService(
                Mock.Of<IConnectionMultiplexer>(),
                payload => {
                    calls++;
                    return calls == 2
                        ? Task.FromException<string>(new InvalidOperationException("OCR failed"))
                        : Task.FromResult("第一段");
                });
            using var stream = Encode(CreateWhiteImage(1000, 9000));

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(stream));

            Assert.Equal(2, calls);
        }

        private static SKBitmap CreateDenseImage(int width, int height) {
            var image = new SKBitmap(width, height);
            for (var y = 0; y < height; y++) {
                for (var x = 0; x < width; x++) {
                    image.SetPixel(x, y, x % 16 < 8 ? SKColors.Black : SKColors.White);
                }
            }
            return image;
        }

        private static SKBitmap CreateWhiteImage(int width, int height) {
            var image = new SKBitmap(width, height);
            image.Erase(SKColors.White);
            return image;
        }

        private static void FillRows(SKBitmap image, int start, int end, SKColor color) {
            for (var y = start; y < end; y++) {
                for (var x = 0; x < image.Width; x++) {
                    image.SetPixel(x, y, color);
                }
            }
        }

        private static MemoryStream Encode(SKBitmap image) {
            using (image) {
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                return new MemoryStream(encoded.ToArray());
            }
        }

        private static void AssertCompleteCoverage(IReadOnlyList<OcrImageSlice> slices, int imageHeight) {
            Assert.Equal(0, slices[0].Top);
            Assert.Equal(imageHeight, slices[^1].Top + slices[^1].Height);
            for (var index = 1; index < slices.Count; index++) {
                Assert.True(slices[index].Top <= slices[index - 1].Top + slices[index - 1].Height);
            }
        }
    }
}

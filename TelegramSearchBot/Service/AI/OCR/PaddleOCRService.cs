using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;
using StackExchange.Redis;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface.AI.OCR;
using TelegramSearchBot.Service.Abstract;

namespace TelegramSearchBot.Service.AI.OCR {
    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public class PaddleOCRService : SubProcessService, IPaddleOCRService, IOCRService {
        private readonly Func<string, Task<string>> _runRpc;

        public new string ServiceName => "PaddleOCRService";

        public OCREngine Engine => OCREngine.PaddleOCR;

        public PaddleOCRService(IConnectionMultiplexer connectionMultiplexer) : base(connectionMultiplexer) {
            ForkName = "OCR";
            _runRpc = RunRpc;
        }

        internal PaddleOCRService(
            IConnectionMultiplexer connectionMultiplexer,
            Func<string, Task<string>> runRpc) : base(connectionMultiplexer) {
            ForkName = "OCR";
            _runRpc = runRpc;
        }

        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(Stream file) {
            using var image = SKBitmap.Decode(file)
                ?? throw new InvalidDataException("无法解码OCR图片");
            var slices = LongImageOcrSlicer.PlanSlices(image);

            if (slices.Count == 1) {
                return await ExecuteSliceAsync(image, slices[0]);
            }

            var results = new List<string>(slices.Count);
            foreach (var slice in slices) {
                results.Add(await ExecuteSliceAsync(image, slice));
            }

            return LongImageOcrSlicer.MergeResults(results);
        }

        private async Task<string> ExecuteSliceAsync(SKBitmap image, OcrImageSlice slice) {
            using var bitmap = ExtractSlice(image, slice);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 99)
                ?? throw new InvalidDataException("无法编码OCR图片");
            return await _runRpc(Convert.ToBase64String(encoded.ToArray()));
        }

        private static SKBitmap ExtractSlice(SKBitmap image, OcrImageSlice slice) {
            if (slice.Top == 0 && slice.Height == image.Height) {
                return image.Copy();
            }

            var bitmap = new SKBitmap(image.Width, slice.Height, image.ColorType, image.AlphaType);
            if (!image.ExtractSubset(bitmap, new SKRectI(0, slice.Top, image.Width, slice.Top + slice.Height))) {
                bitmap.Dispose();
                throw new InvalidDataException("无法裁剪OCR长图");
            }
            return bitmap;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using TelegramSearchBot.Common.Model;
using TelegramSearchBot.Common.Model.DO;

namespace TelegramSearchBot.Manager {
    public class PaddleOCR {
        public PaddleOcrAll all { get; set; }
        private static SemaphoreSlim semaphore = new SemaphoreSlim(1);
        public PaddleOCR() {
            FullOcrModel model = LocalFullModels.ChineseV3;

            all = new PaddleOcrAll(model,
                PaddleDevice.Mkldnn()
                ) {
                AllowRotateDetection = true, /* 允许识别有角度的文字 */
                Enable180Classification = false, /* 允许识别旋转角度大于90度的文字 */
            };
        }

        public PaddleOcrResult GetOcrResult(byte[] image) {
            using (Mat src = Cv2.ImDecode(image, ImreadModes.Color)) {
                PaddleOcrResult result = all.Run(src);
                return result;
            }
        }
        public List<Result> ConvertToResults(PaddleOcrResult paddleOcrResult) {
            var results = new List<Result>();
            foreach (var region in paddleOcrResult.Regions) {
                results.Add(new Result {
                    Text = region.Text,
                    TextRegion = region.Rect.Points().Select(point => {
                        return new List<int>() { ( int ) point.X, ( int ) point.Y };
                    }).ToList(),
                    Confidence = float.IsNaN(region.Score) ? 0 : region.Score,
                });
            }
            return results;
        }
        public PaddleOCRResult Execute(List<string> images) {
            var results = images
                    .Select(Convert.FromBase64String)
                    .Select(GetOcrResult)
                    .Select(ConvertToResults)
                    .ToList();
            return new PaddleOCRResult() {
                Results = results,
                Status = "0",
                Message = "",
            };
        }
        public async Task<PaddleOCRResult> ExecuteAsync(List<string> images) {
            await semaphore.WaitAsync().ConfigureAwait(false);
            var results = await Task.Run<PaddleOCRResult>(() => Execute(images));
            semaphore.Release();
            return results;
        }
    }
}

using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using ZXing.SkiaSharp;

namespace TelegramSearchBot.Service {
    public class AutoQRService : IStreamService, IService {
        public string ServiceName => "AutoQRService";

        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public async Task<string> ExecuteAsync(Stream file) {
            using (var original = SKBitmap.Decode(file)) {

                // create a barcode reader instance
                var reader = new BarcodeReader();
                // detect and decode the barcode inside the bitmap
                var result = reader.Decode(original);
                // do something with the result
                if (result != null) {
                    var txtDecoderType = result.BarcodeFormat.ToString();
                    var txtDecoderContent = result.Text;
                    return result.Text;
                } else {
                    return string.Empty;
                }
            }

            
        }
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
    }
}
